
using System.Collections.Generic;

[System.Serializable]
public class Theme
{
    public string theme_id;
    public string backgroundImg;
    public string logo_path;
    public string QRmobileImg;
    public string CameraImg;
}

[System.Serializable]
public class Booth
{
    public string booth_id;
    public string booth_name;
    public string location;
    public string status;
    public bool login_required;
    public bool payments_enabled;
    public bool decoration_enabled;
    public bool frame_type_tab_enabled;
    public string price;
}

[System.Serializable]
public class BoothResponseData
{
    public Booth booth;
    public Theme theme;
}

[System.Serializable]
public class BoothListResponse
{
    public bool success;
    public string message;
    public BoothResponseData data;
}



#region frameclass
[System.Serializable]
public class FrameAssignment
{
    public string assignment_id;
    public string assignment_type;
}

[System.Serializable]
public class Frame
{
    public string frame_id;
    public string title;
    public string description;
    public string category;
    public string status;
    public string asset_path;
    public string thumb_path;
    public bool sell_in_app;
    public string sale_start_at;
    public string sale_end_at;
    public string price;
    public string submitted_at;
    public string approved_at;
    public int number_of_shots;
    public int number_of_layouts;
    public string review_note;
    public string created_at;
    public string updated_at;
    public string total_uses;
    public Creator creator;
    public Approver approver;
    public string sub_category;
    public List<FrameAssignment> frame_assignments;
    public List<FrameAsset> assets;
}

[System.Serializable]
public class Creator { public string id; public string name; }
[System.Serializable]
public class Approver { public string id; public string name; }

[System.Serializable]
public class FrameAsset
{
    public string frame_asset_id;
    public string asset_id;
    public string type;
    public float x;
    public float y;
    public string width;
    public string height;
    public float scale;
    public float rotation;
    public int layer;
    public int? placeholder_index;
    public int z_index;
    public string path;
}

[System.Serializable]
public class FrameData { public List<Frame> frames; }

[System.Serializable]
public class FrameResponse
{
    public bool success;
    public string message;
    public FrameData data;
}

#endregion

[System.Serializable]
public class GachaFrameResponse
{
    public Frame frame; // Matches your JSON root object
}
