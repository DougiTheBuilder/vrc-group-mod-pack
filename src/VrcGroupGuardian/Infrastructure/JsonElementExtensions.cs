using System.Text.Json;

namespace VrcGroupGuardian.Infrastructure;

public static class JsonElementExtensions
{
    public static bool TryGetString(this JsonElement element, out string? value)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString();
            return true;
        }
        
        value = null;
        return false;
    }
}