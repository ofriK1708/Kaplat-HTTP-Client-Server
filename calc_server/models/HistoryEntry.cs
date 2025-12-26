// ReSharper disable InconsistentNaming
namespace calc_server.models;

public class HistoryEntry
{
    public string? flavor { get; init; }  // "STACK" or "INDEPENDENT"
    public string? operation { get; init; }
    public List<int>? arguments { get; init; }
    public int result { get; init; }

    public static readonly string STACK_FLAVOR = "STACK";
    public static readonly string INDEPENDENT_FLAVOR = "INDEPENDENT";
    public static readonly string PERSISTENCE_POSTGRES = "POSTGRES";
    public static readonly string PERSISTNECE_MONGO = "MONGO";

}