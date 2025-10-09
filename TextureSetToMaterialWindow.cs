// TextureSetToMaterial_UDIM.cs
// Coloque em Assets/Editor/ e abra via Window -> Texture Set To Material (UDIM)

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;

public class TextureSetToMaterial_UDIM : EditorWindow
{
    private DefaultAsset inputFolderObj;
    private DefaultAsset outputFolderObj;
    private Material templateMaterial;
    private bool overwriteExisting = false;
    private bool forceNormalImport = true;

    [MenuItem("Window/Texture Set To Material (UDIM)")]
    public static void OpenWindow()
    {
        GetWindow<TextureSetToMaterial_UDIM>("Texture Set To Material (UDIM)");
    }

    void OnGUI()
    {
        GUILayout.Label("Texture Set → Material (UDIM) - URP Lit", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        inputFolderObj = (DefaultAsset)EditorGUILayout.ObjectField("Input Folder", inputFolderObj, typeof(DefaultAsset), false);
        if (GUILayout.Button("Use selected folder as input")) {
            var sel = Selection.activeObject;
            if (sel != null && AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(sel))) inputFolderObj = (DefaultAsset)sel;
        }

        outputFolderObj = (DefaultAsset)EditorGUILayout.ObjectField("Output Folder", outputFolderObj, typeof(DefaultAsset), false);
        if (GUILayout.Button("Use selected folder as output")) {
            var sel = Selection.activeObject;
            if (sel != null && AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(sel))) outputFolderObj = (DefaultAsset)sel;
        }

        templateMaterial = (Material)EditorGUILayout.ObjectField("Template Material (optional)", templateMaterial, typeof(Material), false);
        overwriteExisting = EditorGUILayout.Toggle("Overwrite existing materials", overwriteExisting);
        forceNormalImport = EditorGUILayout.Toggle("Force normal map importer", forceNormalImport);

        EditorGUILayout.HelpBox("Cria materiais URP/Lit apenas para UDIMs (1001,1002,...). Usa BaseColor, Normal, Height, Emission e Metallic. Ignora somente arquivos que contenham 'roughness'.", MessageType.Info);

        EditorGUILayout.Space();
        if (GUILayout.Button("Create UDIM Materials"))
        {
            if (inputFolderObj == null || outputFolderObj == null) {
                EditorUtility.DisplayDialog("Erro", "Selecione Input e Output folders.", "OK");
                return;
            }

            string inputPath = AssetDatabase.GetAssetPath(inputFolderObj);
            string outputPath = AssetDatabase.GetAssetPath(outputFolderObj);
            ProcessFolder(inputPath, outputPath);
        }
    }

    private readonly HashSet<string> ignoreIfContains = new HashSet<string> {
        "roughness" // somente roughness é ignorado
    };

    private void ProcessFolder(string inputPath, string outputPath)
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { inputPath });
        var groups = new Dictionary<string, UDIMTextureSet>(); // key: udim only

        foreach (var g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            string fileOnly = Path.GetFileNameWithoutExtension(path); // ex: Name_BaseColor_1001 or Name.BaseColor.1001
            string fileLower = fileOnly.ToLower();

            // se conter 'roughness' em qualquer parte, pula
            if (fileLower.Contains("roughness")) continue;

            // detect UDIM patterns: ".1001" at end OR "_1001" at end
            var udimMatch = Regex.Match(fileLower, @"(?:\.|_)(\d{4})$");
            if (!udimMatch.Success) continue; // skip textures without UDIM

            string udim = udimMatch.Groups[1].Value; // ex: 1001
            string beforeUdim = fileLower.Substring(0, udimMatch.Index);

            // clean common clutter like _uv_uv
            beforeUdim = Regex.Replace(beforeUdim, @"_?uv_uv", "", RegexOptions.IgnoreCase).TrimEnd('_');

            // determine suffix token (last token after underscore or dot)
            string[] parts = beforeUdim.Split(new char[] {'_', '.'}, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            string lastToken = parts[parts.Length - 1];

            // identify map type: albedo, normal, height, emission, metallic
            string mapType = null;
            if (lastToken.Contains("base") || lastToken.Contains("albedo") || lastToken.Contains("diffuse")) mapType = "albedo";
            else if (lastToken.Contains("normal") || lastToken == "n" || lastToken.Contains("norm")) mapType = "normal";
            else if (lastToken.Contains("height") || lastToken.Contains("disp") || lastToken.Contains("depth")) mapType = "height";
            else if (lastToken.Contains("emiss") || lastToken.Contains("emit")) mapType = "emission";
            else if (lastToken.Contains("metal") || lastToken.Contains("metallic") || lastToken.Contains("metalness")) mapType = "metallic";

            if (mapType == null) continue; // not a map we care about

            string groupKey = udim; // agrupamento somente por UDIM

            if (!groups.ContainsKey(groupKey)) groups[groupKey] = new UDIMTextureSet(udim);

            groups[groupKey].Add(mapType, path);
        }

        // create materials
        int created = 0;
        foreach (var kv in groups)
        {
            var set = kv.Value;
            string matFileName = set.udim + ".mat";
            string matPathCandidate = Path.Combine(outputPath, matFileName).Replace("\\","/");

            // check existing asset
            var existingMat = AssetDatabase.LoadAssetAtPath<Material>(matPathCandidate);
            if (existingMat != null && !overwriteExisting)
            {
                Debug.Log($"Pular {matPathCandidate} (já existe).");
                continue;
            }

            // create material (prefer template if provided)
            Material mat;
            if (templateMaterial != null) mat = new Material(templateMaterial);
            else
            {
                Shader urp = Shader.Find("Universal Render Pipeline/Lit");
                if (urp == null)
                {
                    // fallback
                    urp = Shader.Find("Standard");
                    Debug.LogWarning("URP Lit shader não encontrado. Criando material com Standard shader. Se estiver usando URP, assegure que o pacote URP está instalado.");
                }
                mat = new Material(urp);
            }

            // assign textures to best-effort properties (tenta várias propriedades para compatibilidade)
            var albedoProps = new string[] { "_BaseMap", "_MainTex", "_BaseColorMap" };
            var normalProps = new string[] { "_BumpMap", "_NormalMap" };
            var heightProps = new string[] { "_ParallaxMap", "_HeightMap" };
            var emissionProps = new string[] { "_EmissionMap", "_EmissiveColorMap", "_EmissiveMap" };

            // Albedo
            if (set.maps.ContainsKey("albedo"))
            {
                Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>(set.maps["albedo"]);
                if (t != null)
                {
                    foreach (var p in albedoProps) { if (mat.HasProperty(p)) { mat.SetTexture(p, t); break; } }
                }
            }

            // Normal
            if (set.maps.ContainsKey("normal"))
            {
                string pth = set.maps["normal"];
                if (forceNormalImport) SetTextureToNormalMap(pth);
                Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>(pth);
                if (t != null)
                {
                    foreach (var p in normalProps) { if (mat.HasProperty(p)) { mat.SetTexture(p, t); mat.EnableKeyword("_NORMALMAP"); break; } }
                }
            }

            // Height
            if (set.maps.ContainsKey("height"))
            {
                Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>(set.maps["height"]);
                if (t != null)
                {
                    foreach (var p in heightProps) { if (mat.HasProperty(p)) { mat.SetTexture(p, t); break; } }
                }
            }

            // Emission
            if (set.maps.ContainsKey("emission"))
            {
                Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>(set.maps["emission"]);
                if (t != null)
                {
                    foreach (var p in emissionProps) { if (mat.HasProperty(p)) { mat.SetTexture(p, t); break; } }
                    mat.EnableKeyword("_EMISSION");
                    if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", Color.white);
                    if (mat.HasProperty("_EmissiveColor")) mat.SetColor("_EmissiveColor", Color.white);
                }
            }

            // Metallic
            if (set.maps.ContainsKey("metallic"))
            {
                Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>(set.maps["metallic"]);
                if (t != null)
                {
                    if (mat.HasProperty("_MetallicGlossMap"))
                    {
                        mat.SetTexture("_MetallicGlossMap", t);
                        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 1f);
                        mat.EnableKeyword("_METALLICGLOSSMAP");
                    }
                    else if (mat.HasProperty("_MaskMap"))
                    {
                        // URP often uses a MaskMap (packed). If user only has metallic, assign it to MaskMap (imperfect).
                        mat.SetTexture("_MaskMap", t);
                    }
                    else if (mat.HasProperty("_Metallic"))
                    {
                        // shader expects float; just set metallic to 1 so it uses some metallic response
                        mat.SetFloat("_Metallic", 1f);
                    }
                }
            }

            // create or overwrite
            if (existingMat != null && overwriteExisting) AssetDatabase.DeleteAsset(matPathCandidate);
            string createdPath = AssetDatabase.GenerateUniqueAssetPath(matPathCandidate);
            AssetDatabase.CreateAsset(mat, createdPath);
            EditorUtility.SetDirty(mat);
            created++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Concluído", $"Materiais criados: {created}\n(Apenas UDIMs processados: 1001,1002,...).", "OK");
    }

    private void SetTextureToNormalMap(string assetPath)
    {
        TextureImporter ti = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (ti != null && ti.textureType != TextureImporterType.NormalMap)
        {
            ti.textureType = TextureImporterType.NormalMap;
            ti.SaveAndReimport();
        }
    }

    private class UDIMTextureSet
    {
        public string udim;
        public Dictionary<string, string> maps = new Dictionary<string, string>(); // mapType -> path
        public UDIMTextureSet(string udim) { this.udim = udim; }

        public void Add(string mapType, string path)
        {
            maps[mapType] = path;
        }
    }
}
