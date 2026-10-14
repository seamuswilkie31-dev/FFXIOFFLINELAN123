using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalXI.Core.Util;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
}
