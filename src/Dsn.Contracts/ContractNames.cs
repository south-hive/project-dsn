using System.Text.RegularExpressions;

namespace Dsn.Contracts;

// Name grammar shared by Envelope, Workspace registration, records and query contracts.
public static class ContractNames
{
    public static bool Workspace(string? value) => value is not null && Regex.IsMatch(value, "^[a-z][a-z0-9-]{0,63}$");
    public static bool Field(string? value) => value is not null && Regex.IsMatch(value, "^[a-z][a-z0-9_]{0,63}$");
}
