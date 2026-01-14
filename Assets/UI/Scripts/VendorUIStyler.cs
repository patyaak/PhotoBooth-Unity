using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class VendorUIStyler : MonoBehaviour
{
    [Header("UI References")]
    public VendorLogin vendorLogin;
    
    [Header("New Assets")]
    public Sprite bgGradient;
    public Sprite cardBg;
    public Sprite buttonBg;
    public Sprite logIcon;
    public Sprite quitIcon;
    public Sprite dropdownBg;

    [Header("Colors")]
    public Color primaryTextColor = Color.black;
    public Color secondaryTextColor = new Color(0.3f, 0.3f, 0.3f);
    public Color accentColor = new Color(0.2f, 0.6f, 1f);

    private void Start()
    {
        if (vendorLogin == null)
            vendorLogin = GetComponent<VendorLogin>();

        ApplyStyles();
    }

    [ContextMenu("Apply Styles")]
    public void ApplyStyles()
    {
        if (vendorLogin == null) return;

        // 1. Background
        if (vendorLogin.backgroundImage != null && bgGradient != null)
        {
            vendorLogin.backgroundImage.sprite = bgGradient;
            vendorLogin.backgroundImage.color = Color.white;
        }

        // 2. Card Container (Login Center)
        // We assume the BoothIDInput and SubmitButton are inside a container.
        // If not, we might need to find a parent panel or just style individual elements.
        // For now, let's try to style the `vendorPanel` if it's the card, or find the card.
        // Best approach: Wrap boothID and submit button in a "Card" at runtime if needed, 
        // or just apply styles to them directly for now.
        
        // Style Input Field
        if (vendorLogin.boothIDInput != null)
        {
            Image inputBg = vendorLogin.boothIDInput.GetComponent<Image>();
            if (inputBg != null && cardBg != null)
            {
                inputBg.sprite = cardBg; // Use card BG style for input too, or a simpler one
                inputBg.type = Image.Type.Sliced;
            }
            
            // Adjust text
            if (vendorLogin.boothIDInput.textComponent != null)
            {
                vendorLogin.boothIDInput.textComponent.color = primaryTextColor;
                vendorLogin.boothIDInput.textComponent.fontSize = 24;
            }
        }

        // Style Submit Button
        if (vendorLogin.submitButton != null)
        {
            Image btnImg = vendorLogin.submitButton.GetComponent<Image>();
            if (btnImg != null && buttonBg != null)
            {
                btnImg.sprite = buttonBg;
                btnImg.type = Image.Type.Sliced;
                btnImg.color = Color.white; // Ensure it uses the sprite's blue gradient
            }

            TMP_Text btnText = vendorLogin.submitButton.GetComponentInChildren<TMP_Text>();
            if (btnText != null)
            {
                btnText.color = Color.white;
                btnText.fontSize = 24;
                btnText.fontStyle = FontStyles.Bold;
            }
        }

        // 3. Top Bar Icons (Log & Quit)
        // We need to find these buttons since they aren't directly ref'd in VendorLogin (yet).
        // Based on the user request, they have "log" and "quit" buttons.
        // We can search for them by name.
        
        StyleButtonByName("LogButton", logIcon);
        StyleButtonByName("QuitButton", quitIcon);
        
        // 4. Dropdowns
        // Search for Dropdowns in the scene (Top right usually)
        TMP_Dropdown[] dropdowns = GetComponentsInChildren<TMP_Dropdown>(true);
        foreach (var dd in dropdowns)
        {
            Image ddImg = dd.GetComponent<Image>();
            if (ddImg != null && dropdownBg != null)
            {
                ddImg.sprite = dropdownBg;
                ddImg.type = Image.Type.Sliced;
            }
        }
    }

    void StyleButtonByName(string btnName, Sprite icon)
    {
        // Try finding recursively or just in children
        Button[] allButtons = GetComponentsInChildren<Button>(true);
        foreach (var btn in allButtons)
        {
            if (btn.name.Contains(btnName) || (btn.name.ToLower().Contains(btnName.ToLower())))
            {
                // Found it
                Image btnImg = btn.GetComponent<Image>();
                if (btnImg != null)
                {
                    // If we want icon ONLY, we might replace the button image or added icon child.
                    // For "Icon Button":
                    // 1. Set Button Image to Transparent (or soft hover circle)
                    // 2. Set/Create Icon Image child
                    
                    // Simple approach: Set button image to the icon
                    if (icon != null)
                    {
                        btnImg.sprite = icon;
                        btnImg.color = Color.white; 
                        
                        // Clear text if any
                        TMP_Text txt = btn.GetComponentInChildren<TMP_Text>();
                        if (txt != null) txt.text = "";
                    }
                }
            }
        }
    }
}
