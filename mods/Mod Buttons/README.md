# ModButtons: The Action Hub Mod

**ModButtons** is a lightweight, centralized action hub for Unity games. It dynamically injects a custom menu popup that allows mods to cleanly integrate their own interactive UI buttons. 

Instead of writing complex Harmony UI patches for every single mod you create, you simply provide a JSON file and your target methods. `ModButtons` handles all the UI generation, layout, visual styling, and asynchronous icon loading.

**Note:** `ModButtons` is entirely standalone. While it is fully compatible with the `ModMenu` framework and will position itself cleanly next to it if detected, `ModMenu` is **NOT** required.

---

## 🛠️ How to Add Your Own Buttons

Adding buttons to the hub requires absolutely zero C# UI code. You just need to create a JSON file in your mod's directory that tells `ModButtons` what methods to execute.

### 1. The Directory Structure
Create a `ModButtons` folder in your mod's root directory. Place your `buttons.json` and any optional icons directly inside it:

```
YourModFolder/
├── ModButtons/
│   ├── buttons.json      <-- Your button configuration
│   └── money_icon.png    <-- (Optional) Button icons
└── YourMod.dll
```

### 2. The `buttons.json` File
This JSON file is a simple array of objects. Every object represents one button you want to add to the menu under your mod's name.

```json
[
  {
    "label": "Add 1,000,000 Yen",
    "codeLabel": "YourMod.Cheat.AddMoney",
    "icon": "money_icon.png",
    "assembly": "YourAwesomeMod",
    "class": "YourAwesomeMod.CheatManager",
    "method": "AddMoney"
  },
  {
    "label": "Restore Idol Stamina",
    "codeLabel": "YourMod.Cheat.RestoreStamina",
    "icon": "",
    "assembly": "YourAwesomeMod",
    "class": "YourAwesomeMod.CheatManager",
    "method": "RestoreStamina"
  }
]
```

**JSON Properties:**
* **`label`**: (Required) The fallback text displayed on the button or tooltip.
* **`codeLabel`**: (Optional) The translation key used for localizing this button's text (see Localization below).
* **`icon`**: (Optional) The exact filename of the PNG to use as the button graphic. Must be in the `ModButtons` folder alongside the JSON. 
* **`assembly`**: (Required) The name of your compiled DLL (without the `.dll` extension).
* **`class`**: (Required) The full namespace and class name where your target method lives.
* **`method`**: (Required) The exact name of the C# method to execute.

### 3. Your Target C# Method
The method you specify in your JSON **must be both `public` and `static`**, and it must take zero arguments.

```csharp
namespace YourAwesomeMod
{
    public class CheatManager
    {
        // This is perfectly valid!
        public static void AddMoney()
        {
            staticVars.company.money += 1000000;
            Debug.Log("Added 1,000,000 Yen!");
        }

        // This will FAIL (Not static)
        public void RestoreStamina() { } 

        // This will FAIL (Requires arguments)
        public static void FireStaff(string staffID) { } 
    }
}
```

---

## 🌍 How to Add Localizations

`ModButtons` features a built-in, multi-mod localization manager. It automatically detects the player's active language settings and translates your button labels, tooltips, and your Mod's title header.

### 1. The Directory Structure
Create a `Localization` folder in your mod's root directory. Inside, create folders for your supported language codes (e.g., `en`, `jp`, `cn`, `ru`).

```
YourModFolder/
├── Localization/
│   ├── en/
│   │   └── strings.txt
│   └── jp/
│       └── strings.txt
├── ModButtons/
│   └── buttons.json
└── YourMod.dll
```

### 2. The `strings.txt` File
The localization file uses a simple `key=value` format.

**Example `Localization/en/strings.txt`:**
```text
# This translates the main header above your grid of buttons
mod.title=My Awesome Cheat Mod

# These translate specific buttons based on the "codeLabel" in your JSON
YourMod.Cheat.AddMoney=Add 1,000,000 Yen
YourMod.Cheat.RestoreStamina=Restore Idol Stamina
```

**Example `Localization/jp/strings.txt`:**
```text
mod.title=素晴らしいチートMOD
YourMod.Cheat.AddMoney=1,000,000円を追加
YourMod.Cheat.RestoreStamina=アイドルのスタミナを回復
```

### 3. Localization Logic
* **No `codeLabel` provided in JSON?** `ModButtons` will automatically generate a translation key using `YourClass.YourMethod`.
* **Missing Translations?** The manager will always load `Localization/en/strings.txt` as a baseline. If the user plays in Japanese but you haven't translated a specific button, it will safely fall back to your English baseline, or ultimately the `label` provided in your JSON.

---

## 🎨 Icon Guidelines
* **Size Requirements:** ModButtons supports buttons anywhere between **64 to 72 pixels** in width and height. You are not forced to use perfect squares—a 64x69 image is perfectly valid! The framework assigns an invisible 72x72 maximum bounding box and dynamically sizes your interactive button element to match your icon's exact pixel dimensions within those limits.
* **Format:** PNG is highly recommended.
* **Fallback:** If you do not provide an icon, `ModButtons` automatically generates a standard, rectangular text-button cloned from the game's native UI style instead of using the icon grid.
