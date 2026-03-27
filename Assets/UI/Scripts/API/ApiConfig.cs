using UnityEngine;

[CreateAssetMenu(
    fileName = "ApiConfig",
    menuName = "Config/API Config",
    order = 1)]
public class ApiConfig : ScriptableObject
{
    public enum EnvironmentType
    {
        Staging,
        Production
    }

    [Header("Environment")]
    public EnvironmentType environment = EnvironmentType.Staging;

    [Header("Base URLs")]
    public string stagingBaseURL =
        "https://photo-stg-api.chvps3.aozora-okinawa.com";

    public string productionBaseURL =
        "https://photoapi.up-t.jp";

    public string BaseURL
    {
        get
        {
            string url = environment == EnvironmentType.Staging
                ? stagingBaseURL
                : productionBaseURL;

            return url.TrimEnd('/');
        }
    }

    public string GetWebSocketURL(bool secure, string boothKey)
    {
        string protocol = secure ? "wss" : "ws";
        string host = environment == EnvironmentType.Staging
            ? "photo-stg-api.chvps3.aozora-okinawa.com"
            : "photoapi.up-t.jp";

        return $"{protocol}://{host}/app/{boothKey}";
    }
}
