namespace DCS.Core.IR;

public enum Lifetime
{
    Singleton,
    Scoped,
    Transient,
    Prototype,
    Request,
    Session,
    Application,
    Unknown
}

public enum Scope
{
    Root,
    Module,
    Framework
}

public enum Confidence
{
    Explicit,
    Inferred,
    Degraded,
    BlindSpot
}

public enum Mechanism
{
    Constructor,
    Property,
    Method,
    Field,
    FactoryParameter,
    DependsFn
}
