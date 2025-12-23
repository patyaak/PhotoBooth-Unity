using UnityEngine;

public static class API
{
    private static ApiConfig _config;

    public static ApiConfig Config
    {
        get
        {
            if (_config == null)
            {
                _config = Resources.Load<ApiConfig>("ApiConfig");

                if (_config == null)
                {
                    Debug.LogError(
                        "❌ ApiConfig.asset not found! " +
                        "Make sure it is inside Assets/Resources/");
                }
            }

            return _config;
        }
    }

    public static string BaseURL
    {
        get
        {
            return Config.BaseURL;
        }
    }
}
