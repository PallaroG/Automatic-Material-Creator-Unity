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
    private string materialNamePrefix = "";
    private bool convertRoughness = false;

    // EditorPrefs keys for UI persistence
    private const string PrefInputPath        = "UDIM_inputPath";
    private const string PrefOutputPath       = "UDIM_outputPath";
    private const string PrefPrefix           = "UDIM_materialPrefix";
    private const string PrefOverwrite        = "UDIM_overwrite";
    private const string PrefForceNormal      = "UDIM_forceNormal";
    private const string PrefConvertRoughness = "UDIM_convertRoughness";

    [MenuItem("Window/Texture Set To Material (UDIM)")]
    public static void OpenWindow()
    {
        GetWindow<TextureSetToMaterial_UDIM>("Texture Set To Material (UDIM)");
    }

    void OnEnable()
    {
        string ip = EditorPrefs.GetString(PrefInputPath, "");
        string op = EditorPrefs.GetString(PrefOutputPath, "");
        if (!string.IsNullOrEmpty(ip)) inputFolderObj  = AssetDatabase.LoadAssetAtPath<DefaultAsset>(ip);
        if (!string.IsNullOrEmpty(op)) outputFolderObj = AssetDatabase.LoadAssetAtPath<DefaultAsset>(op);
        materialNamePrefix = EditorPrefs.GetString(PrefPrefix, "");
        overwriteExisting  = EditorPrefs.GetBool(PrefOverwrite, false);
        forceNormalImport  = EditorPrefs.GetBool(PrefForceNormal, true);
        convertRoughness   = EditorPrefs.GetBool(PrefConvertRoughness, false);
    }

    private void SavePrefs()
    {
        EditorPrefs.SetString(PrefInputPath,        inputFolderObj  != null ? AssetDatabase.GetAssetPath(inputFolderObj)  : "");
        EditorPrefs.SetString(PrefOutputPath,       outputFolderObj != null ? AssetDatabase.GetAssetPath(outputFolderObj) : "");
        EditorPrefs.SetString(PrefPrefix,           materialNamePrefix);
        EditorPrefs.SetBool(PrefOverwrite,          overwriteExisting);
        EditorPrefs.SetBool(PrefForceNormal,        forceNormalImport);
        EditorPrefs.SetBool(PrefConvertRoughness,   convertRoughness);
    }

    void OnGUI()
    {
        GUILayout.Label("Texture Set → Material (UDIM) - URP Lit", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        EditorGUI.BeginChangeCheck();

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

        templateMaterial   = (Material)EditorGUILayout.ObjectField("Template Material (optional)", templateMaterial, typeof(Material), false);
        materialNamePrefix = EditorGUILayout.TextField("Material Name Prefix", materialNamePrefix);
        overwriteExisting  = EditorGUILayout.Toggle("Overwrite existing materials", overwriteExisting);
        forceNormalImport  = EditorGUILayout.Toggle("Force normal map importer", forceNormalImport);
        convertRoughness   = EditorGUILayout.Toggle("Convert Roughness as Smoothness", convertRoughness);

        if (EditorGUI.EndChangeCheck()) SavePrefs();

        EditorGUILayout.HelpBox(
            "Cria materiais URP/Lit para UDIMs (1001,1002,...). " +
            "Mapeia: BaseColor, Normal, Height, Emission, Metallic, Occlusion. " +
            (convertRoughness
                ? "Roughness tratado como Smoothness (sem inversão de canal)."
                : "Roughness ignorado (ative 'Convert Roughness' para incluir)."),
            MessageType.Info);

        EditorGUILayout.Space();
        if (GUILayout.Button("Create UDIM Materials"))
        {
            if (inputFolderObj == null || outputFolderObj == null) {
                EditorUtility.DisplayDialog("Erro", "Selecione Input e Output folders.", "OK");
                return;
            }

            string inputPath  = AssetDatabase.GetAssetPath(inputFolderObj);
            string outputPath = AssetDatabase.GetAssetPath(outputFolderObj);
            ProcessFolder(inputPath, outputPath);
        }
    }

    private void ProcessFolder(string inputPath, string outputPath)
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { inputPath });
        var groups = new Dictionary<string, UDIMTextureSet>();

        foreach (var g in guids)
        {
            string path      = AssetDatabase.GUIDToAssetPath(g);
            string fileOnly  = Path.GetFileNameWithoutExtension(path);
            string fileLower = fileOnly.ToLower();

            // skip roughness unless conversion is enabled
            if (!convertRoughness && fileLower.Contains("roughness")) continue;

            // detect UDIM: _1001 or .1001 at end
            var udimMatch = Regex.Match(fileLower, @"(?:\.|_)(\d{4})$");
            if (!udimMatch.Success) continue;

            string udim       = udimMatch.Groups[1].Value;
            string beforeUdim = fileLower.Substring(0, udimMatch.Index);

            // clean common clutter like _uv_uv
            beforeUdim = Regex.Replace(beforeUdim, @"_?uv_uv", "", RegexOptions.IgnoreCase).TrimEnd('_');

            string[] parts   = beforeUdim.Split(new char[] { '_', '.' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            string lastToken = parts[parts.Length - 1];

            // identify map type
            string mapType = null;
            if      (lastToken.Contains("base")    || lastToken.Contains("albedo")    || lastToken.Contains("diffuse"))   mapType = "albedo";
            else if (lastToken.Contains("normal")   || lastToken == "n"                || lastToken.Contains("norm"))     mapType = "normal";
            else if (lastToken.Contains("height")   || lastToken.Contains("disp")      || lastToken.Contains("depth"))   mapType = "height";
            else if (lastToken.Contains("emiss")    || lastToken.Contains("emit"))                                        mapType = "emission";
            else if (lastToken.Contains("metal")    || lastToken.Contains("metallic")  || lastToken.Contains("metalness")) mapType = "metallic";
            else if (lastToken == "ao"              || lastToken == "occ"               || lastToken.Contains("occlusion")) mapType = "occlusion";
            else if (convertRoughness && (lastToken.Contains("roughness") || lastToken.Contains("rough")))                 mapType = "roughness";

            if (mapType == null) continue;

            if (!groups.ContainsKey(udim)) groups[udim] = new UDIMTextureSet(udim);
            groups[udim].Add(mapType, path);
        }

        int total   = groups.Count;
        int created = 0;
        int index   = 0;

        try
        {
            foreach (var kv in groups)
            {
                var set = kv.Value;
                EditorUtility.DisplayProgressBar(
                    "Criando materiais UDIM",
                    $"Processando UDIM {set.udim} ({index + 1}/{total})",
                    total > 0 ? (float)(index + 1) / total : 1f);

                string formattedPrefix  = string.IsNullOrEmpty(materialNamePrefix) ? "" : materialNamePrefix.TrimEnd('_') + "_";
                string matFileName      = formattedPrefix + set.udim + ".mat";
                string matPathCandidate = Path.Combine(outputPath, matFileName).Replace("\\", "/");

                var existingMat = AssetDatabase.LoadAssetAtPath<Material>(matPathCandidate);
                if (existingMat != null && !overwriteExisting)
                {
                    Debug.Log($"[UDIM] Pulando {matPathCandidate} (já existe).");
                    index++;
                    continue;
                }

                // create material (prefer template if provided)
                Material mat;
                if (templateMaterial != null)
                {
                    mat = new Material(templateMaterial);
                }
                else
                {
                    Shader urp = Shader.Find("Universal Render Pipeline/Lit");
                    if (urp == null)
                    {
                        urp = Shader.Find("Standard");
                        Debug.LogWarning("[UDIM] URP Lit shader não encontrado. Criando material com Standard shader. Se estiver usando URP, assegure que o pacote URP está instalado.");
                    }
                    mat = new Material(urp);
                }

                // --- Albedo ---
                if (set.maps.ContainsKey("albedo"))
                {
                    Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>(set.maps["albedo"]);
                    if (t != null)
                        foreach (var p in new[] { "_BaseMap", "_MainTex", "_BaseColorMap" })
                            if (mat.HasProperty(p)) { mat.SetTexture(p, t); break; }
                }

                // --- Normal ---
                if (set.maps.ContainsKey("normal"))
                {
                    string pth = set.maps["normal"];
                    if (forceNormalImport) SetTextureToNormalMap(pth);
                    Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>(pth);
                    if (t != null)
                        foreach (var p in new[] { "_BumpMap", "_NormalMap" })
                            if (mat.HasProperty(p)) { mat.SetTexture(p, t); mat.EnableKeyword("_NORMALMAP"); break; }
                }

                // --- Height ---
                if (set.maps.ContainsKey("height"))
                {
                    Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>(set.maps["height"]);
                    if (t != null)
                        foreach (var p in new[] { "_ParallaxMap", "_HeightMap" })
                            if (mat.HasProperty(p)) { mat.SetTexture(p, t); break; }
                }

                // --- Emission ---
                if (set.maps.ContainsKey("emission"))
                {
                    Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>(set.maps["emission"]);
                    if (t != null)
                    {
                        foreach (var p in new[] { "_EmissionMap", "_EmissiveColorMap", "_EmissiveMap" })
                            if (mat.HasProperty(p)) { mat.SetTexture(p, t); break; }
                        mat.EnableKeyword("_EMISSION");
                        if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", Color.white);
                        if (mat.HasProperty("_EmissiveColor")) mat.SetColor("_EmissiveColor", Color.white);
                    }
                }

                // --- Metallic ---
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
                            mat.SetTexture("_MaskMap", t);
                        }
                        else if (mat.HasProperty("_Metallic"))
                        {
                            mat.SetFloat("_Metallic", 1f);
                        }
                    }
                }

                // --- Occlusion ---
                if (set.maps.ContainsKey("occlusion"))
                {
                    Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>(set.maps["occlusion"]);
                    if (t != null)
                        foreach (var p in new[] { "_OcclusionMap", "_AmbientOcclusionMap" })
                            if (mat.HasProperty(p)) { mat.SetTexture(p, t); break; }
                }

                // --- Roughness (assigned to smoothness slot without channel inversion) ---
                if (set.maps.ContainsKey("roughness"))
                {
                    Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>(set.maps["roughness"]);
                    if (t != null)
                    {
                        if (mat.HasProperty("_SmoothnessMap"))
                            mat.SetTexture("_SmoothnessMap", t);
                        else
                            Debug.LogWarning($"[UDIM {set.udim}] Roughness encontrado mas o shader não tem '_SmoothnessMap'. Nenhum slot atribuído. URP Lit armazena smoothness no canal alpha do metallic map e não expõe um slot de textura dedicado — use um shader customizado com '_SmoothnessMap' ou faça a conversão/inversão manualmente.");
                    }
                }

                // overwrite or create
                if (existingMat != null && overwriteExisting) AssetDatabase.DeleteAsset(matPathCandidate);
                AssetDatabase.CreateAsset(mat, matPathCandidate);
                EditorUtility.SetDirty(mat);
                created++;
                index++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Concluído", $"Materiais criados: {created} de {total} UDIMs processados.", "OK");
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
        public Dictionary<string, string> maps = new Dictionary<string, string>();
        public UDIMTextureSet(string udim) { this.udim = udim; }

        public void Add(string mapType, string path)
        {
            if (maps.ContainsKey(mapType))
                Debug.LogWarning($"[UDIM {udim}] Mapa '{mapType}' duplicado. '{path}' substituirá '{maps[mapType]}'.");
            maps[mapType] = path;
        }
    }
}
