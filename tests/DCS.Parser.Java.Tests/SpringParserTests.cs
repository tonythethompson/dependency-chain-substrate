using DCS.Core.IR;
using DCS.Core.Parsing;
using DCS.Core.Serialization;
using DCS.Parser.Java;
using DCS.Parser.Java.Naming;
using DCS.Parser.Java.Parsing;
using DCS.Verification;

namespace DCS.Parser.Java.Tests;

public static class JavaFixture
{
    public static string CreateProject(Action<string> writeFiles)
    {
        var root = Path.Combine(Path.GetTempPath(), $"dcs-java-fixture-{Guid.NewGuid():N}");
        var mainJava = Path.Combine(root, "src", "main", "java", "com", "example");
        Directory.CreateDirectory(mainJava);
        writeFiles(mainJava);
        return root;
    }

    public static ContextGraph SingleGraph(ParseResult result) =>
        Assert.Single(result.ContextGraphs);
}

public sealed class TreeSitterJavaParserTests
{
    private const string Sample = """
        package com.example;

        import org.springframework.context.annotation.Bean;
        import org.springframework.context.annotation.Configuration;
        import java.util.List;

        @Configuration
        public class AppConfig {
            @Bean(name = {"mainClient", "legacyClient"})
            public MyService myService(Dep dep) {
                return new MyService();
            }
        }

        class Store<T> {}
        class Dep {}
        class MyService {}
        """;

    [Fact]
    public void Parses_java_source_and_exposes_extractor_shapes()
    {
        using var parser = new TreeSitterJavaParser();
        var (_, root) = parser.Parse(Sample);

        Assert.Contains(JavaNodeWalker.OfType(root, "import_declaration"), n => n.Text.Contains("org.springframework"));
        Assert.Contains(JavaNodeWalker.OfType(root, "method_declaration"), n => n.Text.Contains("@Bean"));
        Assert.Contains(JavaNodeWalker.OfType(root, "constructor_declaration").Concat(JavaNodeWalker.OfType(root, "class_declaration")), _ => true);

        var beanMethod = JavaNodeWalker.OfType(root, "method_declaration").First(n => n.Text.Contains("myService"));
        var beanAnn = JavaNodeWalker.FindAnnotations(beanMethod).First(a => a.Is("Bean"));
        Assert.Equal("mainClient", SpringBeanNameGenerator.ParseBeanNames(beanAnn.Arguments, "ignored").Primary);

        var generic = JavaNodeWalker.OfType(root, "class_declaration").First(n => n.Text.StartsWith("class Store"));
        Assert.Contains('<', JavaNodeWalker.GetExtendsAndImplements(generic).FirstOrDefault() ?? generic.Text);
    }

    [Fact]
    public void Parses_vet_controller_constructor_for_diagnostic()
    {
        var path = Path.Combine(Path.GetTempPath(), "corpus-java-spring",
            "src", "main", "java", "org", "springframework", "samples", "petclinic", "vet", "VetController.java");
        if (!File.Exists(path))
            return;

        using var parser = new TreeSitterJavaParser();
        var source = File.ReadAllText(path);
        var (_, root) = parser.Parse(source);
        var unit = JavaCompilationUnitBuilder.Build(path, "petclinic", SourceSetKind.Main, source, root);
        var type = unit.Types.First(t => t.SimpleName == "VetController");
        Assert.Single(type.Constructors);
        Assert.Single(type.Constructors[0].Parameters);
    }

    [Fact]
    [Trait(CorpusGateTraits.CategoryName, CorpusGateTraits.CategoryValue)]
    [Trait(CorpusGateTraits.CorpusIdName, CorpusGateTraits.JavaSpring)]
    public void PetClinic_parse_stats_when_available()
    {
        var path = CorpusPathResolver.ResolveWithDefaults(
            primaryEnvVar: "CORPUS_JAVA_SPRING_PATH",
            legacyEnvVar: "PETCLINIC_PATH",
            defaultLocalPath: string.Empty,
            tempCloneDirName: "corpus-java-spring",
            workspaceRelativeCheckoutPath: PetClinicPin.CheckoutPath);
        if (path == null)
            return;

        var result = new SpringStaticParser().ParseDirectory(path);
        var graph = result.ContextGraphs.FirstOrDefault();
        if (graph == null)
            return;
        Assert.True(graph.Graph.Nodes.Count > 0);
        // Temporary diagnostic — will assert edges after fix
        Assert.True(graph.Graph.Edges.Count > 0,
            $"Expected wiring edges on PetClinic, got {graph.Graph.Edges.Count} (unresolved={graph.Graph.UnresolvedInjections.Count}).");
    }

    [Fact]
    public void Parses_scope_annotation_and_static_bean_method()
    {
        const string src = """
            package com.example;
            import org.springframework.context.annotation.Bean;
            import org.springframework.context.annotation.Scope;
            import org.springframework.stereotype.Service;
            import org.springframework.boot.autoconfigure.SpringBootApplication;
            @SpringBootApplication
            public class Config {
                @Bean
                public static String token() { return "x"; }
            }
            @Service
            @Scope("prototype")
            class Worker {}
            """;
        using var parser = new TreeSitterJavaParser();
        var (_, root) = parser.Parse(src);
        var unit = JavaCompilationUnitBuilder.Build("App.java", "m", SourceSetKind.Main, src, root);

        var config = unit.Types.First(t => t.SimpleName == "Config");
        Assert.NotEmpty(config.Methods);
        var token = config.Methods.First(m => m.Name == "token");
        Assert.True(token.IsStatic);
        Assert.Contains(token.Annotations, a => a.Is("Bean"));

        var worker = unit.Types.First(t => t.SimpleName == "Worker");
        var scope = worker.Annotations.First(a => a.Is("Scope"));
        Assert.Equal("prototype", scope.Arguments["value"]);
    }
}

public sealed class SpringBeanNameGeneratorTests
{
    [Fact]
    public void URLService_decapitalizes_preserving_consecutive_capitals()
    {
        Assert.Equal("URLService", SpringBeanNameGenerator.Decapitalize("URLService"));
        Assert.Equal("petService", SpringBeanNameGenerator.Decapitalize("PetService"));
    }

    [Fact]
    public void Bean_name_array_uses_first_as_primary()
    {
        var args = new Dictionary<string, string> { ["name"] = "{\"mainClient\", \"legacyClient\"}" };
        var (primary, aliases) = SpringBeanNameGenerator.ParseBeanNames(args, "ignoredMethod");
        Assert.Equal("mainClient", primary);
        Assert.Equal(["legacyClient"], aliases);
    }
}

public sealed class SpringParserFixtureTests
{
    [Fact]
    public void Implements_IStaticParser()
    {
        IStaticParser parser = new SpringStaticParser();
        Assert.NotNull(parser);
    }

    [Fact]
    public void Empty_directory_returns_empty_bundle()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"dcs-java-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var result = new SpringStaticParser().ParseDirectory(dir);
            Assert.Empty(result.ContextGraphs);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Stereotype_inside_scan_root_is_statically_reachable()
    {
        var root = JavaFixture.CreateProject(main =>
        {
            File.WriteAllText(Path.Combine(main, "DemoApplication.java"), """
                package com.example;
                import org.springframework.boot.autoconfigure.SpringBootApplication;
                import org.springframework.stereotype.Service;

                @SpringBootApplication
                public class DemoApplication {}

                @Service
                class PetService {}
                """);
        });

        try
        {
            var graph = JavaFixture.SingleGraph(new SpringStaticParser().ParseDirectory(root));
            var pet = Assert.Single(graph.Graph.Nodes.Where(n => n.PrimaryBeanName == "petService"));
            Assert.Equal(ReachabilityState.StaticallyReachable, pet.ContextMemberships[0].State);
            Assert.Equal(Lifetime.Singleton, pet.Lifetime);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void URLService_stereotype_keeps_bean_name()
    {
        var root = JavaFixture.CreateProject(main =>
        {
            File.WriteAllText(Path.Combine(main, "App.java"), """
                package com.example;
                import org.springframework.boot.autoconfigure.SpringBootApplication;
                import org.springframework.stereotype.Service;
                @SpringBootApplication
                public class App {}
                @Service class URLService {}
                """);
        });

        try
        {
            var graph = JavaFixture.SingleGraph(new SpringStaticParser().ParseDirectory(root));
            var node = Assert.Single(graph.Graph.Nodes.Where(n => n.ExposedType?.ShortName == "URLService"));
            Assert.Equal("URLService", node.PrimaryBeanName);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Two_boot_applications_yield_two_context_graphs()
    {
        var root = JavaFixture.CreateProject(main =>
        {
            File.WriteAllText(Path.Combine(main, "AdminApplication.java"), """
                package com.example;
                import org.springframework.boot.autoconfigure.SpringBootApplication;
                import org.springframework.stereotype.Service;
                @SpringBootApplication
                public class AdminApplication {}
                @Service class AdminOnlyService {}
                """);
            File.WriteAllText(Path.Combine(main, "PublicApplication.java"), """
                package com.example;
                import org.springframework.boot.autoconfigure.SpringBootApplication;
                import org.springframework.stereotype.Service;
                @SpringBootApplication
                public class PublicApplication {}
                @Service class PublicOnlyService {}
                """);
        });

        try
        {
            var result = new SpringStaticParser().ParseDirectory(root);
            Assert.Equal(2, result.ContextGraphs.Count);
            foreach (var cg in result.ContextGraphs)
            {
                Assert.All(cg.Graph.Nodes, n =>
                    Assert.Equal(cg.ContextId, n.ContextMemberships[0].ContextId));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Static_bean_emits_provenance_without_owner_registration_id()
    {
        var root = JavaFixture.CreateProject(main =>
        {
            File.WriteAllText(Path.Combine(main, "Config.java"), """
                package com.example;
                import org.springframework.boot.autoconfigure.SpringBootApplication;
                import org.springframework.context.annotation.Bean;
                import org.springframework.context.annotation.Configuration;
                @SpringBootApplication
                public class Config {
                    @Bean
                    public static String token() { return "x"; }
                }
                """);
        });

        try
        {
            var graph = JavaFixture.SingleGraph(new SpringStaticParser().ParseDirectory(root));
            var prov = Assert.Single(graph.Graph.FactoryProvenance);
            Assert.Equal(FactoryInvocationMode.Static, prov.InvocationMode);
            Assert.Null(prov.OwnerRegistrationId);
            Assert.DoesNotContain(graph.Graph.Edges, e => e.InjectionMechanism == Mechanism.Constructor && e.To == prov.OwnerRegistrationId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Scope_prototype_on_service()
    {
        var root = JavaFixture.CreateProject(main =>
        {
            File.WriteAllText(Path.Combine(main, "App.java"), """
                package com.example;
                import org.springframework.boot.autoconfigure.SpringBootApplication;
                import org.springframework.context.annotation.Scope;
                import org.springframework.stereotype.Service;
                @SpringBootApplication
                public class App {}
                @Service
                @Scope("prototype")
                class Worker {}
                """);
        });

        try
        {
            var graph = JavaFixture.SingleGraph(new SpringStaticParser().ParseDirectory(root));
            var worker = Assert.Single(graph.Graph.Nodes.Where(n => n.ExposedType?.ShortName == "Worker"));
            Assert.Equal(Lifetime.Prototype, worker.Lifetime);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Conditional_configuration_emits_blind_spot()
    {
        var root = JavaFixture.CreateProject(main =>
        {
            File.WriteAllText(Path.Combine(main, "App.java"), """
                package com.example;
                import org.springframework.boot.autoconfigure.SpringBootApplication;
                import org.springframework.boot.autoconfigure.condition.ConditionalOnProperty;
                import org.springframework.context.annotation.Configuration;
                @SpringBootApplication
                public class App {}
                @Configuration
                @ConditionalOnProperty(name = "feature.enabled")
                class FeatureConfig {}
                """);
        });

        try
        {
            var graph = JavaFixture.SingleGraph(new SpringStaticParser().ParseDirectory(root));
            Assert.Contains(graph.Graph.BlindSpots, b => b.Pattern == "conditional_registration");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnableJpaRepositories_makes_repository_statically_reachable()
    {
        var root = JavaFixture.CreateProject(main =>
        {
            File.WriteAllText(Path.Combine(main, "App.java"), """
                package com.example;
                import org.springframework.boot.autoconfigure.SpringBootApplication;
                import org.springframework.data.jpa.repository.JpaRepository;
                import org.springframework.data.jpa.repository.config.EnableJpaRepositories;
                @SpringBootApplication
                @EnableJpaRepositories(basePackages = "com.example")
                public class App {}
                interface OwnerRepository extends JpaRepository<Owner, Long> {}
                class Owner {}
                """);
        });

        try
        {
            var graph = JavaFixture.SingleGraph(new SpringStaticParser().ParseDirectory(root));
            var repo = Assert.Single(graph.Graph.Nodes.Where(n => n.Origin == RegistrationOrigin.SpringData));
            Assert.Equal(ReachabilityState.StaticallyReachable, repo.ContextMemberships[0].State);
            Assert.Equal(MembershipEvidence.RepositoryScan, repo.ContextMemberships[0].Evidence);
            Assert.Null(repo.ImplementationType);
            Assert.Equal(Confidence.Degraded, repo.ParserConfidence);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Determinism_same_graph_when_file_order_shuffled()
    {
        var root = JavaFixture.CreateProject(main =>
        {
            File.WriteAllText(Path.Combine(main, "A.java"), "package com.example; class A {}");
            File.WriteAllText(Path.Combine(main, "B.java"), """
                package com.example;
                import org.springframework.boot.autoconfigure.SpringBootApplication;
                @SpringBootApplication public class B {}
                """);
        });

        try
        {
            var parser = new SpringStaticParser();
            var r1 = parser.ParseDirectory(root);
            var r2 = parser.ParseDirectory(root);
            Assert.Equal(ParseResultSerializer.Serialize(r1), ParseResultSerializer.Serialize(r2));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Old_ir_json_deserializes_with_defaults()
    {
        const string json = """
            {
              "schema_version": "1.1.0",
              "parser_version": "0.1.0",
              "nodes": [],
              "edges": []
            }
            """;
        var graph = IrSerializer.Deserialize(json);
        Assert.NotNull(graph);
        Assert.Empty(graph!.FactoryProvenance);
        Assert.Empty(graph.ConditionalInjections);
    }
}
