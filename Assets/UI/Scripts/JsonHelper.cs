using UnityEngine;

public static class JsonHelper
{
    [System.Serializable]
    private class Wrapper<T>
    {
        public T[] booths;
    }

    public static T[] FromJson<T>(string json)
    {
        string newJson = "{ \"booths\": " + json + "}";
        Wrapper<T> wrapper = JsonUtility.FromJson<Wrapper<T>>(newJson);
        return wrapper.booths;
    }
}
