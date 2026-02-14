#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Collections.Generic; // Added for Dictionary

public class ModelPrefabBatcher : EditorWindow
{
    private string fbxFolder = "Assets/Models/models";
    private static string outputFolder = "Assets/Resources/Models";

    [MenuItem("Tools/Models/Batch Create Prefabs and Link to Menu")] 
    public static void ShowWindow()
    {
        GetWindow<ModelPrefabBatcher>("Model Prefab Batcher");
    }

    private void OnGUI()
    {
        GUILayout.Label("Batch Create FBX Prefabs", EditorStyles.boldLabel);
        fbxFolder = EditorGUILayout.TextField("FBX Folder", fbxFolder);
        outputFolder = EditorGUILayout.TextField("Output Prefabs Folder", outputFolder);

        if (GUILayout.Button("Create Prefabs"))
        {
            CreatePrefabs(fbxFolder, outputFolder);
        }

        if (GUILayout.Button("Fix Materials in Existing Prefabs"))
        {
            FixMaterialsInPrefabs(fbxFolder);
        }

        if (GUILayout.Button("Create Materials from Textures"))
        {
            CreateMaterialsFromTextures(fbxFolder);
        }

        if (GUILayout.Button("Debug Paths and Refresh"))
        {
            DebugPathsAndRefresh(fbxFolder);
        }

        if (GUILayout.Button("Link Prefabs to ModelMenuController (scene)"))
        {
            LinkPrefabsToMenu();
        }
    }

    public static void CreatePrefabs(string sourceFolder, string destFolder)
    {
        if (!Directory.Exists(destFolder))
        {
            Directory.CreateDirectory(destFolder);
            AssetDatabase.Refresh();
        }

        string[] fbxGuids = AssetDatabase.FindAssets("t:Model", new[] { sourceFolder });
        int created = 0;
        foreach (var guid in fbxGuids)
        {
            string fbxPath = AssetDatabase.GUIDToAssetPath(guid);
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbx == null) continue;

            var temp = PrefabUtility.InstantiatePrefab(fbx) as GameObject;
            temp.name = fbx.name;

            string prefabPath = Path.Combine(destFolder, fbx.name + ".prefab").Replace("\\", "/");
            PrefabUtility.SaveAsPrefabAsset(temp, prefabPath);
            GameObject.DestroyImmediate(temp);
            created++;
        }
        AssetDatabase.Refresh();
        Debug.Log($"Создано префабов: {created} → {destFolder}");
    }

    public static void LinkPrefabsToMenu()
    {
        var menu = GameObject.FindFirstObjectByType<ModelMenuController>();
        if (menu == null)
        {
            Debug.LogError("ModelMenuController не найден в сцене. Открой сцену с меню.");
            return;
        }

        var prefabs = Resources.LoadAll<GameObject>("Models");
        if (prefabs == null || prefabs.Length == 0)
        {
            Debug.LogError("В Resources/Models нет префабов. Сначала создайте их.");
            return;
        }

        int count = Mathf.Min(menu.models.Length, prefabs.Length);
        for (int i = 0; i < count; i++)
        {
            if (menu.models[i] == null) menu.models[i] = new ModelData();
            menu.models[i].modelName = prefabs[i].name;
            menu.models[i].modelPrefab = prefabs[i];
        }

        // Обновим подписи кнопок
        for (int i = 0; i < menu.menuButtons.Length && i < menu.models.Length; i++)
        {
            var btn = menu.menuButtons[i];
            if (btn == null) continue;
            var txt = btn.GetComponentInChildren<UnityEngine.UI.Text>();
            if (txt != null && menu.models[i] != null)
            {
                txt.text = menu.models[i].modelName;
            }
        }

        EditorUtility.SetDirty(menu);
        Debug.Log($"Привязано {count} префабов к ModelMenuController.");
    }

    public static void FixMaterialsInPrefabs(string fbxFolder)
    {
        if (!Directory.Exists(outputFolder))
        {
            Debug.LogError($"Папка {outputFolder} не существует!");
            return;
        }

        string[] prefabFiles = Directory.GetFiles(outputFolder, "*.prefab", SearchOption.TopDirectoryOnly);
        int fixedCount = 0;

        foreach (string prefabPath in prefabFiles)
        {
            if (FixPrefabMaterials(prefabPath, fbxFolder))
            {
                fixedCount++;
            }
        }

        AssetDatabase.Refresh();
        Debug.Log($"Исправлено материалов в {fixedCount} префабах");
    }

    public static void CreateMaterialsFromTextures(string fbxFolder)
    {
        if (!Directory.Exists(fbxFolder))
        {
            Debug.LogError($"Папка {fbxFolder} не существует!");
            return;
        }

        // Ищем все папки с моделями
        string[] modelFolders = Directory.GetDirectories(fbxFolder);
        int createdCount = 0;

        foreach (string modelFolder in modelFolders)
        {
            string materialsPath = Path.Combine(modelFolder, "Materials").Replace("\\", "/");
            Debug.Log($"Проверяю папку: {materialsPath}");
            
            if (!Directory.Exists(materialsPath))
            {
                Debug.LogWarning($"Папка Materials не найдена в: {modelFolder}");
                continue; // Пропускаем папки без Materials
            }

            string modelName = Path.GetFileName(modelFolder);
            Debug.Log($"Обрабатываю модель: {modelName}");

            // Группируем текстуры по частям модели
            var textureGroups = GroupTexturesByPart(materialsPath);
            
            foreach (var group in textureGroups)
            {
                if (CreateMaterialFromTextureGroup(group.Key, group.Value, materialsPath))
                {
                    createdCount++;
                }
            }
        }

        AssetDatabase.Refresh();
        Debug.Log($"Создано материалов: {createdCount}");
    }

    private static Dictionary<string, List<string>> GroupTexturesByPart(string materialsPath)
    {
        var groups = new Dictionary<string, List<string>>();
        
        // Ищем все PNG файлы
        string[] pngFiles = Directory.GetFiles(materialsPath, "*.png", SearchOption.TopDirectoryOnly);
        
        foreach (string pngPath in pngFiles)
        {
            string fileName = Path.GetFileNameWithoutExtension(pngPath);
            
            // Парсим имя файла: TEX_Solenoid_valve_Body_Base -> Body
            string[] parts = fileName.Split('_');
            if (parts.Length >= 4)
            {
                string partName = parts[parts.Length - 2]; // Предпоследняя часть
                
                if (!groups.ContainsKey(partName))
                {
                    groups[partName] = new List<string>();
                }
                groups[partName].Add(pngPath);
            }
        }
        
        return groups;
    }

    private static bool CreateMaterialFromTextureGroup(string partName, List<string> texturePaths, string materialsPath)
    {
        // Создаем имя материала
        string materialName = $"MAT_{partName}";
        string materialPath = Path.Combine(materialsPath, $"{materialName}.mat");
        
        // Проверяем, не существует ли уже материал
        if (File.Exists(materialPath))
        {
            Debug.Log($"Материал уже существует: {materialPath}");
            return false;
        }

        // Создаем новый материал
        Material material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        if (material == null)
        {
            Debug.LogWarning($"Не удалось найти URP/Lit шейдер, используем Standard");
            material = new Material(Shader.Find("Standard"));
        }

        // Находим и назначаем текстуры
        foreach (string texturePath in texturePaths)
        {
            string fileName = Path.GetFileNameWithoutExtension(texturePath);
            
            if (fileName.EndsWith("_Base"))
            {
                Texture2D baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                if (baseTex != null)
                {
                    if (material.HasProperty("_MainTex"))
                        material.SetTexture("_MainTex", baseTex);
                    if (material.HasProperty("_BaseMap"))
                        material.SetTexture("_BaseMap", baseTex);
                }
            }
            else if (fileName.EndsWith("_Normal"))
            {
                Texture2D normalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                if (normalTex != null)
                {
                    if (material.HasProperty("_BumpMap"))
                        material.SetTexture("_BumpMap", normalTex);
                    if (material.HasProperty("_NormalMap"))
                        material.SetTexture("_NormalMap", normalTex);
                }
            }
            else if (fileName.EndsWith("_Metallic"))
            {
                Texture2D metallicTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                if (metallicTex != null)
                {
                    if (material.HasProperty("_MetallicGlossMap"))
                        material.SetTexture("_MetallicGlossMap", metallicTex);
                }
            }
        }

        // Сохраняем материал
        AssetDatabase.CreateAsset(material, materialPath);
        Debug.Log($"Создан материал: {materialPath}");
        
        return true;
    }

    private static bool FixPrefabMaterials(string prefabPath, string fbxFolder)
    {
        GameObject prefabObject = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefabObject == null)
        {
            Debug.LogWarning($"Не удалось загрузить префаб: {prefabPath}");
            return false;
        }

        // Получаем имя модели из имени префаба
        string modelName = prefabObject.name;
        Debug.Log($"Обрабатываю префаб: {modelName}");
        
        // Ищем соответствующую папку с материалами в исходной папке моделей
        string materialsPath = Path.Combine(fbxFolder, modelName, "Materials").Replace("\\", "/");
        
        Debug.Log($"Ищем папку материалов: {materialsPath}");
        Debug.Log($"Проверяю существование: {Directory.Exists(materialsPath)}");
        
        // Дополнительная проверка - ищем папку по-другому
        if (!Directory.Exists(materialsPath))
        {
            // Пробуем найти папку по-другому
            string[] possiblePaths = {
                materialsPath,
                materialsPath.Replace("/", "\\"),
                Path.Combine(fbxFolder, modelName, "Materials").Replace("\\", "/"),
                Path.Combine(fbxFolder, modelName, "Materials")
            };
            
            // Добавляем поиск по альтернативным именам
            string[] alternativeNames = {
                modelName,
                modelName.Replace(" ", "_"),
                modelName.Replace("_", " "),
                modelName.Replace(" ", "-"),
                modelName.Replace("-", " "),
                modelName.ToLower(),
                modelName.ToUpper()
            };
            
            bool found = false;
            
            // Сначала пробуем стандартные пути
            foreach (string path in possiblePaths)
            {
                if (Directory.Exists(path))
                {
                    materialsPath = path.Replace("\\", "/");
                    Debug.Log($"Найдена папка по стандартному пути: {materialsPath}");
                    found = true;
                    break;
                }
            }
            
            // Если не нашли, пробуем альтернативные имена
            if (!found)
            {
                foreach (string altName in alternativeNames)
                {
                    if (altName == modelName) continue; // Пропускаем оригинальное имя
                    
                    string altPath = Path.Combine(fbxFolder, altName, "Materials").Replace("\\", "/");
                    Debug.Log($"Пробую альтернативное имя: '{altName}' -> {altPath}");
                    
                    if (Directory.Exists(altPath))
                    {
                        materialsPath = altPath;
                        Debug.Log($"Найдена папка по альтернативному имени: {materialsPath}");
                        found = true;
                        break;
                    }
                }
            }
            
            if (!found)
            {
                Debug.LogWarning($"Папка материалов не найдена для {modelName}: {materialsPath}");
                Debug.LogWarning($"Проверяю содержимое папки: {fbxFolder}");
                
                if (Directory.Exists(fbxFolder))
                {
                    string[] subdirs = Directory.GetDirectories(fbxFolder);
                    Debug.Log($"Подпапки в {fbxFolder}: {string.Join(", ", subdirs)}");
                    
                    foreach (string subdir in subdirs)
                    {
                        string subdirName = Path.GetFileName(subdir);
                        Debug.Log($"Подпапка: '{subdirName}' (длина: {subdirName.Length})");
                        
                        if (subdirName.Equals(modelName, System.StringComparison.OrdinalIgnoreCase))
                        {
                            Debug.Log($"Найдено точное совпадение: '{subdirName}' == '{modelName}'");
                        }
                        
                        // Проверяем альтернативные варианты
                        foreach (string altName in alternativeNames)
                        {
                            if (subdirName.Equals(altName, System.StringComparison.OrdinalIgnoreCase))
                            {
                                Debug.Log($"Найдено альтернативное совпадение: '{subdirName}' == '{altName}'");
                            }
                        }
                    }
                }
                return false;
            }
        }

        // Находим все MeshRenderer'ы в иерархии
        MeshRenderer[] renderers = prefabObject.GetComponentsInChildren<MeshRenderer>();
        Debug.Log($"Найдено MeshRenderer'ов в {modelName}: {renderers.Length}");
        
        // Логируем все renderer'ы для отладки
        for (int i = 0; i < renderers.Length; i++)
        {
            Debug.Log($"Renderer {i}: {renderers[i].name} (материалов: {renderers[i].sharedMaterials.Length})");
        }
        
        bool anyFixed = false;

        foreach (MeshRenderer renderer in renderers)
        {
            Debug.Log($"Обрабатываю renderer: {renderer.name}");
            
            // Проверяем каждый материал в renderer'е
            Material[] materials = renderer.sharedMaterials;
            Debug.Log($"У renderer'а {renderer.name} материалов: {materials.Length}");
            bool materialsFixed = false;

            for (int i = 0; i < materials.Length; i++)
            {
                Debug.Log($"Материал {i}: {(materials[i] != null ? materials[i].name : "NULL")}");
                
                if (materials[i] != null)
                {
                    // Проверяем, есть ли у материала текстуры
                    if (HasValidTextures(materials[i]))
                    {
                        Debug.Log($"Материал {materials[i].name} уже имеет текстуры, пропускаю");
                        continue; // Материал уже правильный
                    }
                    else
                    {
                        Debug.Log($"Материал {materials[i].name} не имеет текстур, ищу замену");
                    }
                }
                else
                {
                    Debug.Log($"Материал {i} равен NULL, ищу замену");
                }

                // Пытаемся найти правильный материал по имени
                Material fixedMaterial = FindMaterialByName(renderer.name, materialsPath, i);
                if (fixedMaterial != null)
                {
                    Debug.Log($"Найден подходящий материал: {fixedMaterial.name} для {renderer.name}");
                    materials[i] = fixedMaterial;
                    materialsFixed = true;
                    Debug.Log($"Исправлен материал для {renderer.name} в {prefabObject.name}");
                }
                else
                {
                    Debug.LogWarning($"Не найден подходящий материал для {renderer.name} в {materialsPath}");
                }
            }

            // Применяем исправленные материалы
            if (materialsFixed)
            {
                Debug.Log($"Применяю исправленные материалы к {renderer.name}");
                renderer.sharedMaterials = materials;
                anyFixed = true;
            }
            else
            {
                Debug.Log($"Материалы для {renderer.name} не были исправлены");
            }
        }

        // Сохраняем префаб если что-то исправлено
        if (anyFixed)
        {
            PrefabUtility.SaveAsPrefabAsset(prefabObject, prefabPath);
            return true;
        }

        return false;
    }

    private static bool HasValidTextures(Material material)
    {
        if (material == null) return false;

        Debug.Log($"Проверяю материал {material.name} на наличие текстур:");
        
        bool hasMainTexture = false;
        
        // Проверяем основные текстуры (ОБЯЗАТЕЛЬНЫЕ для отображения)
        if (material.HasProperty("_MainTex"))
        {
            Texture2D mainTex = material.GetTexture("_MainTex") as Texture2D;
            Debug.Log($"  _MainTex: {(mainTex != null ? mainTex.name : "NULL")}");
            if (mainTex != null) hasMainTexture = true;
        }
        
        if (material.HasProperty("_BaseMap"))
        {
            Texture2D baseTex = material.GetTexture("_BaseMap") as Texture2D;
            Debug.Log($"  _BaseMap: {(baseTex != null ? baseTex.name : "NULL")}");
            if (baseTex != null) hasMainTexture = true;
        }
        
        if (material.HasProperty("_AlbedoMap"))
        {
            Texture2D albedoTex = material.GetTexture("_AlbedoMap") as Texture2D;
            Debug.Log($"  _AlbedoMap: {(albedoTex != null ? albedoTex.name : "NULL")}");
            if (albedoTex != null) hasMainTexture = true;
        }

        // Проверяем другие важные текстуры (ДОПОЛНИТЕЛЬНЫЕ)
        if (material.HasProperty("_BumpMap"))
        {
            Texture2D bumpTex = material.GetTexture("_BumpMap") as Texture2D;
            Debug.Log($"  _BumpMap: {(bumpTex != null ? bumpTex.name : "NULL")}");
        }
        
        if (material.HasProperty("_NormalMap"))
        {
            Texture2D normalTex = material.GetTexture("_NormalMap") as Texture2D;
            Debug.Log($"  _NormalMap: {(normalTex != null ? normalTex.name : "NULL")}");
        }

        if (hasMainTexture)
        {
            Debug.Log($"  Материал {material.name} ИМЕЕТ основную текстуру - ВАЛИДЕН");
            return true;
        }
        else
        {
            Debug.Log($"  Материал {material.name} НЕ имеет основной текстуры - НЕ ВАЛИДЕН");
            return false;
        }
    }

    private static Material FindMaterialByName(string rendererName, string materialsPath, int materialIndex)
    {
        Debug.Log($"Ищу материал для renderer: {rendererName} в папке: {materialsPath}");
        
        if (!Directory.Exists(materialsPath))
        {
            Debug.LogWarning($"Папка материалов не найдена: {materialsPath}");
            return null;
        }

        // Ищем все .mat файлы в папке материалов
        string[] materialFiles = Directory.GetFiles(materialsPath, "*.mat", SearchOption.TopDirectoryOnly);
        Debug.Log($"Найдено .mat файлов в {materialsPath}: {materialFiles.Length}");
        
        foreach (string matPath in materialFiles)
        {
            Debug.Log($"Проверяю материал: {Path.GetFileName(matPath)}");
        }

        // Сначала пытаемся найти материал по точному имени renderer'а
        foreach (string matPath in materialFiles)
        {
            string fileName = Path.GetFileNameWithoutExtension(matPath);
            Debug.Log($"Проверяю точное совпадение: '{fileName}' == '{rendererName}'");

            // Точное совпадение имени
            if (fileName.Equals(rendererName, System.StringComparison.OrdinalIgnoreCase))
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (material != null && HasValidTextures(material))
                {
                    Debug.Log($"Найдено точное совпадение: {fileName}");
                    return material;
                }
                else
                {
                    Debug.LogWarning($"Материал {fileName} найден, но не имеет текстур или равен NULL");
                }
            }
        }

        // Затем ищем по частичному совпадению
        foreach (string matPath in materialFiles)
        {
            string fileName = Path.GetFileNameWithoutExtension(matPath);
            Debug.Log($"Проверяю частичное совпадение: '{fileName}' содержит '{rendererName}' или наоборот");

            // Проверяем различные варианты именования
            if (fileName.Contains(rendererName, System.StringComparison.OrdinalIgnoreCase) ||
                rendererName.Contains(fileName, System.StringComparison.OrdinalIgnoreCase))
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (material != null && HasValidTextures(material))
                {
                    Debug.Log($"Найдено частичное совпадение: {fileName}");
                    return material;
                }
            }
        }

        // Ищем по ключевым словам в имени
        string[] keywords = { "body", "main", "base", "primary", "default" };
        foreach (string keyword in keywords)
        {
            Debug.Log($"Ищу по ключевому слову: {keyword}");
            foreach (string matPath in materialFiles)
            {
                string fileName = Path.GetFileNameWithoutExtension(matPath);

                if (fileName.Contains(keyword, System.StringComparison.OrdinalIgnoreCase))
                {
                    Material material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    if (material != null && HasValidTextures(material))
                    {
                        Debug.Log($"Найдено по ключевому слову '{keyword}': {fileName}");
                        return material;
                    }
                }
            }
        }

        // Если не нашли по имени, берем первый валидный материал
        foreach (string matPath in materialFiles)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (material != null && HasValidTextures(material))
            {
                Debug.Log($"Использован материал по умолчанию: {Path.GetFileName(matPath)} для {rendererName}");
                return material;
            }
        }

        Debug.LogWarning($"Не найден подходящий материал для {rendererName} в {materialsPath}");
        return null;
    }

    public static void DebugPathsAndRefresh(string fbxFolder)
    {
        Debug.Log($"FBX Folder: {fbxFolder}");
        Debug.Log($"Output Folder: {outputFolder}");
        Debug.Log($"FBX Folder Exists: {Directory.Exists(fbxFolder)}");
        Debug.Log($"Output Folder Exists: {Directory.Exists(outputFolder)}");
        Debug.Log($"FBX Folder Content: {string.Join(", ", Directory.GetDirectories(fbxFolder))}");
        Debug.Log($"Output Folder Content: {string.Join(", ", Directory.GetDirectories(outputFolder))}");
        AssetDatabase.Refresh();
        Debug.Log("AssetDatabase refreshed.");
    }
}
#endif
