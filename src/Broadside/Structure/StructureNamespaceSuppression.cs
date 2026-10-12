using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Scope = "namespace",
    Target = "~N:Broadside.Structure",
    Justification = "The namespace list of spec #33 (Implementation Decisions, Namespaces) names this area Structure; it collides only with a Visual Basic keyword, and VB callers can escape it.")]
