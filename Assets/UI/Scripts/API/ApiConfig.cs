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
}
