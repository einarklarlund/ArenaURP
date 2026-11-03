string Dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../Temp/nt-weapon-previews"));
string[] NAMES = { "Grenade Shotgun", "Splinter Gun", "Auto Crossbow", "Super Crossbow", "Splinter Pistol", "Super Splinter Gun", "Hyper Rifle", "Heavy Revolver", "Gatling Slugger", "Super Slugger", "Quadruple Machinegun", "Double Minigun" };
var ims = new System.Collections.Generic.List<Texture2D>();
foreach (var n in NAMES) { var t = new Texture2D(2, 2); t.LoadImage(System.IO.File.ReadAllBytes(Dir + "/" + n + ".png")); ims.Add(t); }
int w = ims[0].width, h = ims[0].height;
var sheet = new Texture2D(w, h * ims.Count, TextureFormat.RGB24, false);
for (int i = 0; i < ims.Count; i++) sheet.SetPixels(0, (ims.Count - 1 - i) * h, w, h, ims[i].GetPixels());
sheet.Apply();
System.IO.File.WriteAllBytes(Dir + "/Batch 3 previews.png", sheet.EncodeToPNG());
return "ok";
