using System.Text;
using System.Text.Json;
using DCS.Analysis;
using DCS.Core.IR;
using DCS.Core.Serialization;

namespace DCS.Viz;

public static class HtmlVizGenerator
{
    public static string Generate(RegistrationGraph graph, AnalysisResult? analysis = null)
    {
        var graphJson = IrSerializer.Serialize(graph);
        var analysisJson = analysis != null
            ? JsonSerializer.Serialize(analysis, IrSerializer.Options)
            : "null";

        var commit = graph.CommitSha ?? "working directory";
        var title = $"DCS Graph — {commit[..Math.Min(8, commit.Length)]}";

        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <title>{{title}}</title>
            <style>
            *{box-sizing:border-box;margin:0;padding:0}
            body{font-family:system-ui,sans-serif;background:#0f172a;color:#e2e8f0;display:flex;height:100vh;overflow:hidden}
            #sidebar{width:280px;min-width:200px;background:#1e293b;padding:16px;overflow-y:auto;display:flex;flex-direction:column;gap:12px;border-right:1px solid #334155}
            #sidebar h1{font-size:14px;font-weight:700;color:#94a3b8;text-transform:uppercase;letter-spacing:.05em}
            #sidebar .stat{display:flex;justify-content:space-between;font-size:13px;padding:4px 0;border-bottom:1px solid #1e293b}
            #sidebar .stat-label{color:#64748b}
            #sidebar .stat-value{font-weight:600;color:#e2e8f0}
            #sidebar .legend-item{display:flex;align-items:center;gap:8px;font-size:13px;padding:3px 0}
            #sidebar .legend-dot{width:10px;height:10px;border-radius:50%;flex-shrink:0}
            #node-detail{margin-top:8px;padding:10px;background:#0f172a;border-radius:6px;font-size:12px;line-height:1.6;display:none}
            #node-detail .det-name{font-weight:700;font-size:13px;color:#f8fafc;margin-bottom:4px}
            #node-detail .det-row{display:flex;gap:6px;color:#94a3b8}
            #node-detail .det-val{color:#e2e8f0}
            #canvas-wrap{flex:1;position:relative;overflow:hidden}
            canvas{display:block;width:100%;height:100%}
            #hint{position:absolute;bottom:12px;left:50%;transform:translateX(-50%);font-size:12px;color:#475569;pointer-events:none}
            .err-badge{background:#dc2626;color:#fff;font-size:11px;padding:2px 6px;border-radius:4px;margin-left:6px}
            .warn-badge{background:#d97706;color:#fff;font-size:11px;padding:2px 6px;border-radius:4px;margin-left:6px}
            </style>
            </head>
            <body>
            <div id="sidebar">
              <h1>DCS Graph</h1>
              <div id="stats"></div>
              <h1 style="margin-top:8px">Legend</h1>
              <div id="legend"></div>
              <h1 style="margin-top:8px">Selected Node</h1>
              <div id="node-detail"><em style="color:#64748b">Click a node to inspect</em></div>
            </div>
            <div id="canvas-wrap">
              <canvas id="c"></canvas>
              <div id="hint">Scroll to zoom · Drag to pan · Click node for details</div>
            </div>
            <script>
            const GRAPH = {{graphJson}};
            const ANALYSIS = {{analysisJson}};

            const FW_COLORS = {
              winui:         '#ef4444',
              avalonia:      '#3b82f6',
              wpf:           '#8b5cf6',
              aspnetcore:    '#10b981',
              msdi:          '#6b7280',
              'ms-extensions':'#9ca3af',
              none:          '#475569'
            };

            const CONF_ALPHA = { explicit: 1, inferred: 0.75, degraded: 0.5, blind_spot: 0.3 };

            // ── Build node/edge maps ─────────────────────────────────────────
            const nodeMap = {};
            GRAPH.nodes.forEach(n => { nodeMap[n.id] = n; });

            const outEdges = {}, inEdges = {};
            GRAPH.edges.forEach(e => {
              (outEdges[e.from] = outEdges[e.from] || []).push(e);
              (inEdges[e.to]   = inEdges[e.to]   || []).push(e);
            });

            // Error/warn sets from analysis
            const leakedIds = new Set(ANALYSIS ? ANALYSIS.leaked.map(l => l.node_id) : []);
            const brokenIds = new Set(ANALYSIS ? ANALYSIS.broken_chains.map(b => b.node_id) : []);
            const orphanIds = new Set(ANALYSIS ? ANALYSIS.orphaned.map(o => o.node_id) : []);

            // ── Layout: group by framework ───────────────────────────────────
            const groups = {};
            GRAPH.nodes.forEach(n => {
              const tag = (n.framework_tags && n.framework_tags[0]) || 'none';
              (groups[tag] = groups[tag] || []).push(n);
            });

            const W = window.innerWidth - 280, H = window.innerHeight;
            const cx = W / 2, cy = H / 2;
            const groupKeys = Object.keys(groups).sort();
            const R = Math.min(cx, cy) * 0.55;

            groupKeys.forEach((tag, gi) => {
              const angle = (gi / groupKeys.length) * 2 * Math.PI - Math.PI / 2;
              const gcx = cx + R * Math.cos(angle);
              const gcy = cy + R * Math.sin(angle);
              const gNodes = groups[tag];
              const r = Math.max(40, Math.sqrt(gNodes.length) * 18);
              gNodes.forEach((node, ni) => {
                const na = (ni / gNodes.length) * 2 * Math.PI;
                node._x = gcx + r * Math.cos(na);
                node._y = gcy + r * Math.sin(na);
              });
            });

            // ── Canvas setup ─────────────────────────────────────────────────
            const canvas = document.getElementById('c');
            const ctx = canvas.getContext('2d');

            let dpr = window.devicePixelRatio || 1;
            function resize() {
              dpr = window.devicePixelRatio || 1;
              canvas.width  = (window.innerWidth - 280) * dpr;
              canvas.height = window.innerHeight * dpr;
              canvas.style.width  = (window.innerWidth - 280) + 'px';
              canvas.style.height = window.innerHeight + 'px';
              draw();
            }
            window.addEventListener('resize', resize);
            resize();

            // ── Pan / zoom ───────────────────────────────────────────────────
            let zoom = 1, panX = 0, panY = 0;
            let dragging = false, dragSX = 0, dragSY = 0, dragPX = 0, dragPY = 0;

            canvas.addEventListener('wheel', e => {
              e.preventDefault();
              const rect = canvas.getBoundingClientRect();
              const mx = e.clientX - rect.left, my = e.clientY - rect.top;
              const factor = e.deltaY < 0 ? 1.12 : 1 / 1.12;
              panX = mx - (mx - panX) * factor;
              panY = my - (my - panY) * factor;
              zoom *= factor;
              draw();
            }, { passive: false });

            canvas.addEventListener('mousedown', e => {
              dragging = true; dragSX = e.clientX; dragSY = e.clientY;
              dragPX = panX; dragPY = panY;
            });
            window.addEventListener('mousemove', e => {
              if (!dragging) return;
              panX = dragPX + (e.clientX - dragSX);
              panY = dragPY + (e.clientY - dragSY);
              draw();
            });
            window.addEventListener('mouseup', () => { dragging = false; });

            // ── Selection ────────────────────────────────────────────────────
            let selected = null;

            canvas.addEventListener('click', e => {
              const rect = canvas.getBoundingClientRect();
              const mx = (e.clientX - rect.left - panX) / zoom;
              const my = (e.clientY - rect.top  - panY) / zoom;
              let hit = null, bestD = 14;
              GRAPH.nodes.forEach(n => {
                const d = Math.hypot(n._x - mx, n._y - my);
                if (d < bestD) { bestD = d; hit = n; }
              });
              selected = hit;
              draw();
              showDetail(hit);
            });

            function showDetail(n) {
              const el = document.getElementById('node-detail');
              if (!n) { el.innerHTML = '<em style="color:#64748b">Click a node to inspect</em>'; el.style.display=''; return; }
              el.style.display = '';
              const badgeHtml = leakedIds.has(n.id) ? '<span class="err-badge">LEAKED</span>' :
                                brokenIds.has(n.id) ? '<span class="err-badge">BROKEN</span>' :
                                orphanIds.has(n.id) ? '<span class="warn-badge">ORPHANED</span>' : '';
              el.innerHTML = `
                <div class="det-name">${esc(n.display_name)}${badgeHtml}</div>
                ${n.concrete_impl ? `<div class="det-row">impl <span class="det-val">${esc(n.concrete_impl.short_name)}</span></div>` : ''}
                <div class="det-row">lifetime <span class="det-val">${n.lifetime}</span></div>
                <div class="det-row">confidence <span class="det-val">${n.parser_confidence}</span></div>
                ${n.framework_tags?.length ? `<div class="det-row">frameworks <span class="det-val">${n.framework_tags.join(', ')}</span></div>` : ''}
                ${n.source_location ? `<div class="det-row">source <span class="det-val">${esc(n.source_location.file_path)}:${n.source_location.line||''}</span></div>` : ''}
                <div class="det-row">out-edges <span class="det-val">${(outEdges[n.id]||[]).length}</span>  in-edges <span class="det-val">${(inEdges[n.id]||[]).length}</span></div>
              `;
            }

            function esc(s) { return (s||'').replace(/&/g,'&amp;').replace(/</g,'&lt;'); }

            // ── Sidebar stats + legend ────────────────────────────────────────
            function buildSidebar() {
              const s = document.getElementById('stats');
              const leaked = ANALYSIS ? ANALYSIS.leaked.length : '?';
              const broken = ANALYSIS ? ANALYSIS.broken_chains.length : '?';
              const orphaned = ANALYSIS ? ANALYSIS.orphaned.length : '?';
              s.innerHTML = [
                ['Nodes', GRAPH.nodes.length],
                ['Edges', GRAPH.edges.length],
                ['Blind spots', GRAPH.blind_spots.length],
                ['Leaked', leaked],
                ['Broken chains', broken],
                ['Orphaned', orphaned],
              ].map(([k,v]) => `<div class="stat"><span class="stat-label">${k}</span><span class="stat-value">${v}</span></div>`).join('');

              const leg = document.getElementById('legend');
              leg.innerHTML = Object.entries(FW_COLORS).map(([tag, color]) => {
                const count = (groups[tag]||[]).length;
                if (!count) return '';
                return `<div class="legend-item"><div class="legend-dot" style="background:${color}"></div>${tag} <span style="color:#64748b;margin-left:auto">${count}</span></div>`;
              }).join('');
            }
            buildSidebar();

            // ── Drawing ────────────────────────────────────────────────────────
            const NODE_R = 6;

            function nodeColor(n) {
              const tag = (n.framework_tags && n.framework_tags[0]) || 'none';
              return FW_COLORS[tag] || FW_COLORS.none;
            }

            function draw() {
              const W = canvas.width, H = canvas.height;
              ctx.clearRect(0, 0, W, H);
              ctx.save();
              ctx.scale(dpr, dpr);
              ctx.translate(panX, panY);
              ctx.scale(zoom, zoom);

              const selectedNeighborIds = selected ? new Set([
                ...(outEdges[selected.id]||[]).map(e => e.to),
                ...(inEdges[selected.id] ||[]).map(e => e.from),
              ]) : null;

              // Draw edges
              GRAPH.edges.forEach(e => {
                const src = nodeMap[e.from], dst = nodeMap[e.to];
                if (!src || !dst) return;
                const highlight = selected && (e.from === selected.id || e.to === selected.id);
                ctx.beginPath();
                ctx.moveTo(src._x, src._y);
                ctx.lineTo(dst._x, dst._y);
                ctx.strokeStyle = highlight ? '#f59e0b' : '#334155';
                ctx.lineWidth = highlight ? 1.5 / zoom : 0.5 / zoom;
                ctx.globalAlpha = highlight ? 0.9 : 0.4;
                ctx.stroke();
                ctx.globalAlpha = 1;
              });

              // Draw nodes
              GRAPH.nodes.forEach(n => {
                const isSelected = selected && n.id === selected.id;
                const isNeighbor = selectedNeighborIds && selectedNeighborIds.has(n.id);
                const isError = leakedIds.has(n.id) || brokenIds.has(n.id);
                const isWarn = orphanIds.has(n.id);
                const alpha = selected ? (isSelected || isNeighbor ? 1 : 0.25) : (CONF_ALPHA[n.parser_confidence] || 0.7);

                ctx.globalAlpha = alpha;
                ctx.beginPath();
                const r = isSelected ? NODE_R * 1.6 : NODE_R;
                ctx.arc(n._x, n._y, r, 0, 2 * Math.PI);
                ctx.fillStyle = isError ? '#ef4444' : isWarn ? '#f59e0b' : nodeColor(n);
                ctx.fill();

                if (isSelected) {
                  ctx.strokeStyle = '#f8fafc';
                  ctx.lineWidth = 2 / zoom;
                  ctx.stroke();
                }
                ctx.globalAlpha = 1;
              });

              // Labels at zoom > threshold
              if (zoom > 1.2) {
                ctx.font = `${Math.min(12, 10 / zoom * zoom)}px system-ui`;
                ctx.textAlign = 'center';
                GRAPH.nodes.forEach(n => {
                  const isSelected = selected && n.id === selected.id;
                  const isNeighbor = selectedNeighborIds && selectedNeighborIds.has(n.id);
                  if (!isSelected && !isNeighbor && zoom < 2) return;
                  ctx.globalAlpha = selected ? (isSelected || isNeighbor ? 1 : 0) : 0.9;
                  ctx.fillStyle = '#e2e8f0';
                  ctx.fillText(n.display_name, n._x, n._y - NODE_R - 3);
                  ctx.globalAlpha = 1;
                });
              }

              ctx.restore();
            }

            draw();
            </script>
            </body>
            </html>
            """;
    }
}
