using Newtonsoft.Json;

namespace GVFS.Common
{
    public class VersionResponse
    {
        public string Version { get; set; }

        public static VersionResponse FromJsonString(string jsonString)
        {
            JsonSerializer serializer = new JsonSerializer();
            return JsonConvert.DeserializeObject<VersionResponse>(jsonString);
        }
    }
}
