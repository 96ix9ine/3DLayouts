using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Rendering;
using System.Collections;

public class HoverTooltipHighlighter : MonoBehaviour
{
    [Header("Setup")]
    public Camera playerCamera;
    public GameObject tooltipContainerPrefab; // Resources/Prefabs/TooltipContainer

    [Header("Raycast")]
    public LayerMask raycastMask = -1;
    public float maxRaycastDistance = 1000f;

    [Header("Highlight")]
    public Color highlightColor = Color.yellow;
    public float pulseSpeed = 4f;
    public float minEmission = 0.25f;
    public float maxEmission = 1.25f;
    public bool tryOutline = true;
    public float outlineWidth = 1.5f;
    public bool useOverlayHighlight = true; // использовать overlay материал для подсветки в билде
    public bool debugOverlay = true; // отладочные логи для overlay

    [Header("Tooltip positioning")]
    public float worldOffset = 0.05f;
    public float minScale = 0.0008f;
    public float maxScale = 0.002f;
    public float minTooltipOffset = 0.01f;   // базовый сдвиг вправо при близком зуме
    public float maxTooltipOffset = 0.02f;   // базовый сдвиг вправо при дальнем зуме
    public float minDepthOffset = 0f;    // выдвигать к камере при близком зуме
    public float maxDepthOffset = 0.12f;    // при дальнем зуме
    public float minVerticalOffset = 0.02f; // базовый сдвиг вверх при близком зуме
    public float maxVerticalOffset = 0.06f; // базовый сдвиг вверх при дальнем зуме

    [Header("Tooltip positioning multipliers (angle-based)")]
    public float horizontalAngleMultiplier = 20f; // множитель для динамического X от угла
    public float verticalAngleMultiplier = 10f;   // множитель для динамического Y от угла
    public float depthAngleMultiplier = 5f;      // множитель для динамического Z от угла
    public float minDepthToCamera = 0.08f;       // минимальный итоговый вынос к камере, чтобы не зарываться в геометрию

    [Header("Smart depth to camera")]
    public bool useDynamicMinDepth = true;       // плавный минимум от зума
    public float minDepthClose = 0.15f;          // минимум при максимальном приближении
    public float minDepthFar = 0.28f;            // минимум при максимальном отдалении
    public bool useObstructionAwareDepth = true; // выталкивать тултип перед ближайшей преградой от камеры
    public float obstructionPadding = 0.15f;     // на сколько выдвинуть перед преградой

    [Header("Tooltip auto size")]
    public float minWidth = 60f;
    public float maxWidth = 300f;
    public float minHeight = 20f;
    public float maxHeight = 200f;
    public float paddingX = 20f;
    public float paddingY = 10f;

    [Header("Tooltip view clamp")]
    public bool fitTooltipInView = true;     // удерживать тултип в пределах экрана
    public int screenMargin = 16;            // отступ от краёв экрана в пикселях
    public bool allowScaleDownToFit = true;  // разрешить уменьшать масштаб для влезания

    private readonly Dictionary<string, string> partNameToTooltip = new Dictionary<string, string>();
    private readonly List<Renderer> tempRenderers = new List<Renderer>();

    private Renderer currentRenderer;
    private MaterialPropertyBlock currentBlock;
    private GameObject tooltipInstance;
    private TextMeshProUGUI tooltipText;
    private RectTransform tooltipRect;
    private LayoutElement tooltipLayout;
    private ModelController modelController;
    private GameObject currentOverlay; // overlay объект для подсветки
    private static readonly HashSet<string> ExcludedColliderNames = new HashSet<string> { "Manometer_glass_1", "Manometer_glass_2" };

    public void Initialize(GameObject modelRoot, ModelData modelData, Camera camera)
    {
        playerCamera = camera != null ? camera : Camera.main;
        modelController = FindAnyObjectByType<ModelController>();

        // Заполняем маппинг имён -> текст
        partNameToTooltip.Clear();
        if (modelData != null && modelData.partTooltips != null)
        {
            foreach (var e in modelData.partTooltips)
            {
                if (!string.IsNullOrEmpty(e.partName))
                {
                    partNameToTooltip[e.partName] = e.tooltip;
                }
            }
        }

        // Кэширум префаб тултипа
        if (tooltipContainerPrefab == null)
        {
            // В билде Resources.Load чувствителен к пути/регистрy
            tooltipContainerPrefab = Resources.Load<GameObject>("Prefabs/TooltipContainer");
            if (tooltipContainerPrefab == null)
            {
                tooltipContainerPrefab = Resources.Load<GameObject>("Prefabs/tooltipcontainer");
            }
        }

        // Автоколлайдеры (только MeshFilter/Renderer)
        AddCollidersIfMissing(modelRoot);
    }

    void Update()
    {
        if (playerCamera == null) return;

        Ray ray = playerCamera.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out var hit, maxRaycastDistance, raycastMask, QueryTriggerInteraction.Ignore))
        {
            var hitRenderer = GetRendererForHit(hit.collider);

            // Фильтруем только объекты текущей модели
            if (hitRenderer != null && hitRenderer.transform.IsChildOf(transform))
            {
                if (hitRenderer != currentRenderer)
                {
                    ClearHighlight();
                    ApplyHighlight(hitRenderer);
                }

                UpdateTooltip(hitRenderer, hit.point);
                return;
            }
        }

        // Если не попали по модели — очистить
        ClearHighlight();
        HideTooltip();
    }

    Renderer GetRendererForHit(Collider col)
    {
        if (col == null) return null;

        // 1) На самом объекте
        var r = col.GetComponent<Renderer>();
        if (r != null && r.enabled && r.gameObject.activeInHierarchy) return r;

        // 2) Поднимаемся вверх до корня модели
        Transform t = col.transform;
        while (t != null && t != transform)
        {
            r = t.GetComponent<Renderer>();
            if (r != null && r.enabled && r.gameObject.activeInHierarchy) return r;
            t = t.parent;
        }

        // 3) Спускаемся вниз — берём первый видимый Renderer в сабдетях коллайдера
        r = col.GetComponentInChildren<Renderer>();
        if (r != null && r.enabled && r.gameObject.activeInHierarchy && r.transform.IsChildOf(transform)) return r;

        return null;
    }

    void AddCollidersIfMissing(GameObject root)
    {
        if (root == null) return;

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            // Для некоторых деталей отключаем MeshCollider и не создаём новый
            if (ExcludedColliderNames.Contains(r.name))
            {
                var meshColliders = r.GetComponents<MeshCollider>();
                for (int i = 0; i < meshColliders.Length; i++)
                {
                    if (meshColliders[i] != null) meshColliders[i].enabled = false;
                }
                continue;
            }

            // Пробуем найти MeshFilter
            var mf = r.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                if (r.GetComponent<Collider>() == null)
                {
                    var mc = r.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    mc.convex = false;
                }
                continue;
            }

            // Поддержка SkinnedMeshRenderer: подложим BoxCollider, чтобы ховер работал
            var smr = r as SkinnedMeshRenderer;
            if (smr != null)
            {
                if (r.GetComponent<Collider>() == null)
                {
                    var bc = r.gameObject.AddComponent<BoxCollider>();
                    // Примерная аппроксимация размеров по bounds в мире
                    var b = r.bounds;
                    bc.size = r.transform.InverseTransformVector(b.size);
                    bc.center = r.transform.InverseTransformPoint(b.center);
                }
            }
        }
    }

    void ApplyHighlight(Renderer r)
    {
        currentRenderer = r;
        if (currentBlock == null) currentBlock = new MaterialPropertyBlock();

        // Включаем эмиссию на всех материалах рендера через property block
        float emissionStrength = Mathf.Lerp(minEmission, maxEmission, 0.5f);
        Color emission = highlightColor * Mathf.LinearToGammaSpace(emissionStrength);
        currentRenderer.GetPropertyBlock(currentBlock);
        currentBlock.SetColor("_EmissionColor", emission);
        // Fallback для случаев, когда эмиссия/вариант шейдера вырезан в билде
        currentBlock.SetColor("_BaseColor", highlightColor);
        currentBlock.SetColor("_Color", highlightColor);
        if (tryOutline)
        {
            currentBlock.SetColor("_OutlineColor", highlightColor);
            currentBlock.SetFloat("_OutlineWidth", outlineWidth);
        }
        currentRenderer.SetPropertyBlock(currentBlock);

        // Обеспечим ключевое слово эмиссии (придётся инстанцировать materials)
        var mats = currentRenderer.materials; // создаёт копии в рантайме, но нужно для ключей в билде
        for (int i = 0; i < mats.Length; i++)
        {
            var mat = mats[i];
            if (mat != null)
            {
                // Пробуем включить эмиссию; если вариант шейдера вырезан — хотя бы BaseColor будет работать через PropertyBlock
                mat.EnableKeyword("_EMISSION");
                if (tryOutline)
                {
                    // Если шейдер поддерживает outline, часто нужны ключи
                    mat.EnableKeyword("_OUTLINE_ON");
                }
            }
        }

        // Создаём overlay для гарантированной подсветки в билде
        if (useOverlayHighlight)
        {
            CreateOverlayHighlight(r);
        }

        // Старт пульсации через Update
        CancelInvoke(nameof(PulseEmission));
        InvokeRepeating(nameof(PulseEmission), 0f, 0.016f);
    }

    void PulseEmission()
    {
        if (currentRenderer == null) return;

        float t = (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f; // 0..1
        float intensity = Mathf.Lerp(minEmission, maxEmission, t);
        Color emission = highlightColor * Mathf.LinearToGammaSpace(intensity);
        currentRenderer.GetPropertyBlock(currentBlock);
        currentBlock.SetColor("_EmissionColor", emission);
        if (tryOutline)
        {
            // Лёгкая пульсация толщины контура
            currentBlock.SetFloat("_OutlineWidth", outlineWidth * Mathf.Lerp(0.9f, 1.1f, t));
        }
        currentRenderer.SetPropertyBlock(currentBlock);
    }

    void ClearHighlight()
    {
        if (currentRenderer == null) return;

        CancelInvoke(nameof(PulseEmission));
        // Останавливаем все корутины
        StopAllCoroutines();
        
        if (currentBlock == null) currentBlock = new MaterialPropertyBlock();
        currentRenderer.GetPropertyBlock(currentBlock);
        currentBlock.Clear();
        currentRenderer.SetPropertyBlock(currentBlock);

        // Отключаем ключ для всех материалов, чтобы вернуть исходное состояние
        foreach (var mat in currentRenderer.materials)
        {
            if (mat != null)
            {
                mat.DisableKeyword("_EMISSION");
                if (tryOutline)
                {
                    mat.DisableKeyword("_OUTLINE_ON");
                }
            }
        }

        // Убираем overlay
        if (currentOverlay != null)
        {
            DestroyImmediate(currentOverlay);
            currentOverlay = null;
        }

        currentRenderer = null;
    }

    void UpdateTooltip(Renderer r, Vector3 hitPoint)
    {
        if (tooltipContainerPrefab == null) return;

        // Определяем текст
        string text = null;
        if (!partNameToTooltip.TryGetValue(r.name, out text))
        {
            // Пытаемся найти по ближайшему имени вверх по иерархии
            var t = r.transform;
            while (t != null && t != transform && string.IsNullOrEmpty(text))
            {
                if (partNameToTooltip.TryGetValue(t.name, out text)) break;
                t = t.parent;
            }
        }

        if (string.IsNullOrEmpty(text))
        {
            HideTooltip();
            return;
        }

        if (tooltipInstance == null)
        {
            tooltipInstance = Instantiate(tooltipContainerPrefab);
            // Переводим в world-space canvas
            var canvas = tooltipInstance.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                var canvasObj = new GameObject("HoverTooltipCanvas");
                canvasObj.transform.SetParent(transform);
                var c = canvasObj.AddComponent<Canvas>();
                c.renderMode = RenderMode.WorldSpace;
                c.worldCamera = playerCamera != null ? playerCamera : Camera.main;
                c.sortingOrder = 2000;
                var scaler = canvasObj.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                var gr = canvasObj.AddComponent<GraphicRaycaster>();
                tooltipInstance.transform.SetParent(canvasObj.transform, false);
            }
            tooltipText = tooltipInstance.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            tooltipRect = tooltipInstance.GetComponent<RectTransform>();
            tooltipLayout = tooltipInstance.GetComponent<LayoutElement>();

            // В билде иногда отваливаются шрифты — проверим материалы TMP
            if (tooltipText != null && tooltipText.fontMaterial != null)
            {
                var mat = tooltipText.fontMaterial;
                if (mat.HasProperty("_FaceDilate")) mat.SetFloat("_FaceDilate", Mathf.Clamp(mat.GetFloat("_FaceDilate"), -1f, 1f));
            }

            // Настроим материалы тултипа так, чтобы он всегда был поверх геометрии
            ConfigureTooltipMaterials();
        }

        if (tooltipText != null)
        {
            tooltipText.text = text;
            AutoSizeTooltip();
        }

        tooltipInstance.SetActive(true);

        // Масштаб/позиция зависят от зума
        float zoomT = 0.5f;
        if (modelController != null)
        {
            zoomT = Mathf.InverseLerp(modelController.minZoomDistance, modelController.maxZoomDistance, modelController.CurrentCameraDistance);
        }

        // Масштаб (с горизонтом min/max, позже можем подсжать в fitTooltipInView)
        float scale = Mathf.Lerp(minScale, maxScale, zoomT);
        tooltipInstance.transform.localScale = Vector3.one * scale;

        // Выставляем позицию: правее и выше точки + ближе к камере
        Vector3 camPos = playerCamera.transform.position;
        Vector3 toCam = (camPos - hitPoint).normalized;
        Vector3 right = playerCamera.transform.right;
        Vector3 up = Vector3.up;

        float baseLateral = Mathf.Lerp(minTooltipOffset, maxTooltipOffset, zoomT);
        float baseDepth = Mathf.Lerp(minDepthOffset, maxDepthOffset, zoomT);
        float baseVertical = Mathf.Lerp(minVerticalOffset, maxVerticalOffset, zoomT);

        // Доп. смещение от угла обзора (если смотрим сбоку — сильнее уводим вправо/вверх)
        Vector3 camToPoint = (hitPoint - camPos).normalized;
        float viewAngle = Vector3.Angle(playerCamera.transform.forward, camToPoint);
        float angleT = Mathf.Clamp01(viewAngle / 90f);
        float dynamicX = baseLateral * horizontalAngleMultiplier * angleT;
        float dynamicY = baseVertical * verticalAngleMultiplier * angleT;
        float dynamicZ = baseDepth * Mathf.Lerp(0.5f, -0.5f, angleT) * depthAngleMultiplier;

        // Динамический минимум глубины от зума (или фиксированный)
        float dynamicMinDepth = minDepthToCamera;
        if (useDynamicMinDepth)
        {
            dynamicMinDepth = Mathf.Lerp(minDepthClose, minDepthFar, zoomT);
        }

        // Итоговый Z-сдвиг к камере с безопасным минимумом
        float finalDepth = Mathf.Max(dynamicMinDepth, baseDepth + dynamicZ + worldOffset);

        Vector3 worldPos = hitPoint
            + right * (baseLateral + dynamicX)
            + up    * (baseVertical + dynamicY)
            + toCam * finalDepth;

        // Выталкивание перед ближайшей преградой между камерой и тултипом
        if (useObstructionAwareDepth)
        {
            Vector3 camToPos = worldPos - camPos;
            float camToPosDist = camToPos.magnitude;
            if (camToPosDist > 0.0001f)
            {
                Vector3 dir = camToPos / camToPosDist;
                if (Physics.Raycast(camPos, dir, out var blockHit, camToPosDist, raycastMask, QueryTriggerInteraction.Ignore))
                {
                    float safeDist = Mathf.Max(playerCamera.nearClipPlane + 0.01f, blockHit.distance - obstructionPadding);
                    worldPos = camPos + dir * safeDist;
                }
            }
        }

        tooltipInstance.transform.position = worldPos;

        // Гарантируем, что тултип остаётся в видимой области камеры
        if (fitTooltipInView && tooltipRect != null && playerCamera != null)
        {
            // Переводим текущую позицию в экранные координаты
            Vector3 screenPos = playerCamera.WorldToScreenPoint(worldPos);
            bool behind = screenPos.z < Mathf.Max(playerCamera.nearClipPlane + 0.01f, 0.01f);

            // Если оказался за камерой — разворачиваем перед камерой на near + offset
            if (behind)
            {
                Vector3 forward = playerCamera.transform.forward;
                worldPos = playerCamera.transform.position + forward * (playerCamera.nearClipPlane + 0.05f);
                tooltipInstance.transform.position = worldPos;
                screenPos = playerCamera.WorldToScreenPoint(worldPos);
            }

            // Текущий пиксельный размер тултипа с учётом worldScale
            float currentScale = tooltipInstance.transform.lossyScale.x;
            Vector2 pixelSize = new Vector2(tooltipRect.rect.width, tooltipRect.rect.height) * currentScale * 100f; // Canvas WorldSpace: 1 unit = 100 px примерно

            float minX = screenMargin + pixelSize.x * 0.5f;
            float maxX = Screen.width - screenMargin - pixelSize.x * 0.5f;
            float minY = screenMargin + pixelSize.y * 0.5f;
            float maxY = Screen.height - screenMargin - pixelSize.y * 0.5f;

            // Если не влезает — сначала пытаемся подсжать масштаб
            if (allowScaleDownToFit)
            {
                float scaleX = 1f, scaleY = 1f;
                if (pixelSize.x > (Screen.width - screenMargin * 2))
                {
                    scaleX = (Screen.width - screenMargin * 2) / Mathf.Max(pixelSize.x, 1f);
                }
                if (pixelSize.y > (Screen.height - screenMargin * 2))
                {
                    scaleY = (Screen.height - screenMargin * 2) / Mathf.Max(pixelSize.y, 1f);
                }
                float fitScale = Mathf.Min(scaleX, scaleY, 1f);
                if (fitScale < 1f)
                {
                    tooltipInstance.transform.localScale *= fitScale;
                    currentScale = tooltipInstance.transform.lossyScale.x;
                    pixelSize = new Vector2(tooltipRect.rect.width, tooltipRect.rect.height) * currentScale * 100f;
                    minX = screenMargin + pixelSize.x * 0.5f;
                    maxX = Screen.width - screenMargin - pixelSize.x * 0.5f;
                    minY = screenMargin + pixelSize.y * 0.5f;
                    maxY = Screen.height - screenMargin - pixelSize.y * 0.5f;
                }
            }

            // Корректируем экранную позицию в границы
            screenPos.x = Mathf.Clamp(screenPos.x, minX, maxX);
            screenPos.y = Mathf.Clamp(screenPos.y, minY, maxY);

            // Обратно в мир
            Vector3 clampedWorld = playerCamera.ScreenToWorldPoint(screenPos);

            // Гарантированный зазор от near-plane
            Vector3 camDir = (clampedWorld - playerCamera.transform.position);
            float dist = camDir.magnitude;
            if (dist < playerCamera.nearClipPlane + 0.02f)
            {
                Vector3 d = camDir.normalized;
                clampedWorld = playerCamera.transform.position + d * (playerCamera.nearClipPlane + 0.02f);
            }

            tooltipInstance.transform.position = clampedWorld;
        }

        // Явное: yaw + pitch (X разрешён), roll (Z) заблокирован мировым up => текст не переворачивается
        Vector3 dirToCamera = camPos - worldPos;
        if (dirToCamera.sqrMagnitude < 0.0001f)
        {
            dirToCamera = playerCamera.transform.forward;
        }
        // Используем up-ось камеры, чтобы зафиксировать roll и исключить flip на крайних углах
        Vector3 cameraUp = playerCamera != null ? playerCamera.transform.up : Vector3.up;
        tooltipInstance.transform.rotation = Quaternion.LookRotation(dirToCamera, cameraUp);
    }

    void ConfigureTooltipMaterials()
    {
        if (tooltipInstance == null) return;
        var renderers = tooltipInstance.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            var mats = r.materials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null) continue;
                if (m.HasProperty("_ZTest")) m.SetFloat("_ZTest", (float)CompareFunction.Always);
                if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
                m.renderQueue = 4000; // Overlay
            }
        }
    }

    void HideTooltip()
    {
        if (tooltipInstance != null)
        {
            tooltipInstance.SetActive(false);
        }
    }

    void AutoSizeTooltip()
    {
        if (tooltipText == null || tooltipInstance == null) return;

        // Получаем предпочтительные размеры текста
        var settings = tooltipText.GetTextInfo(tooltipText.text);
        tooltipText.ForceMeshUpdate();
        float preferredW = tooltipText.preferredWidth + paddingX;
        float preferredH = tooltipText.preferredHeight + paddingY;

        float width = Mathf.Clamp(preferredW, minWidth, maxWidth);
        float height = Mathf.Clamp(preferredH, minHeight, maxHeight);

        if (tooltipLayout != null)
        {
            tooltipLayout.preferredWidth = width;
            tooltipLayout.preferredHeight = height;
        }

        if (tooltipRect != null)
        {
            tooltipRect.sizeDelta = new Vector2(width, height);
        }
    }

    void CreateOverlayHighlight(Renderer r)
    {
        if (currentOverlay != null) return;

        // Создаём копию меша с прозрачным жёлтым материалом
        var meshFilter = r.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null) return;

        currentOverlay = new GameObject($"Overlay_{r.name}");
        currentOverlay.transform.SetParent(r.transform);
        currentOverlay.transform.localPosition = Vector3.zero;
        currentOverlay.transform.localRotation = Quaternion.identity;
        currentOverlay.transform.localScale = Vector3.one;

        var overlayMeshFilter = currentOverlay.AddComponent<MeshFilter>();
        overlayMeshFilter.sharedMesh = meshFilter.sharedMesh;

        var overlayRenderer = currentOverlay.AddComponent<MeshRenderer>();
        
        // Создаём простой материал для overlay
        var overlayMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        if (overlayMaterial == null)
        {
            overlayMaterial = new Material(Shader.Find("Sprites/Default"));
        }
        
        overlayMaterial.color = new Color(highlightColor.r, highlightColor.g, highlightColor.b, 0.3f);
        overlayMaterial.SetFloat("_ZTest", 0f); // Always pass ZTest
        overlayMaterial.SetFloat("_ZWrite", 0f);
        overlayMaterial.renderQueue = 3000; // Рендерим поверх основного материала
        
        // Дополнительные настройки для URP
        if (overlayMaterial.HasProperty("_Surface"))
        {
            overlayMaterial.SetFloat("_Surface", 1f); // Transparent
        }
        if (overlayMaterial.HasProperty("_Blend"))
        {
            overlayMaterial.SetFloat("_Blend", 0f); // Alpha
        }
        if (overlayMaterial.HasProperty("_SrcBlend"))
        {
            overlayMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        }
        if (overlayMaterial.HasProperty("_DstBlend"))
        {
            overlayMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        }
        
        overlayRenderer.material = overlayMaterial;
        
        if (debugOverlay) 
        {
            Debug.Log($"[HoverHL] Создан overlay для {r.name}");
            Debug.Log($"[HoverHL] Материал: {overlayMaterial.name}, шейдер: {overlayMaterial.shader.name}");
            Debug.Log($"[HoverHL] Начальный цвет: {overlayMaterial.color}, alpha: {overlayMaterial.color.a}");
        }
        
        // Запускаем пульсацию overlay
        StartCoroutine(PulseOverlay());
    }

    System.Collections.IEnumerator PulseOverlay()
    {
        if (currentOverlay == null) 
        {
            if (debugOverlay) Debug.Log("[HoverHL] PulseOverlay: currentOverlay is null");
            yield break;
        }
        
        var renderer = currentOverlay.GetComponent<Renderer>();
        if (renderer == null) 
        {
            if (debugOverlay) Debug.Log("[HoverHL] PulseOverlay: renderer is null");
            yield break;
        }

        if (debugOverlay) Debug.Log($"[HoverHL] PulseOverlay запущена для {currentOverlay.name}");

        while (currentOverlay != null && currentOverlay.activeInHierarchy && renderer != null)
        {
            // Проверяем, что объект ещё существует
            if (currentOverlay == null || renderer == null) 
            {
                if (debugOverlay) Debug.Log("[HoverHL] PulseOverlay: объект уничтожен, выходим");
                break;
            }
            
            float t = (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f;
            float alpha = Mathf.Lerp(0.2f, 0.6f, t);
            
            try
            {
                var color = renderer.material.color;
                color.a = alpha;
                renderer.material.color = color;
                
                if (debugOverlay && Time.frameCount % 60 == 0) // каждую секунду при 60 FPS
                {
                    Debug.Log($"[HoverHL] PulseOverlay: alpha = {alpha:F2}, t = {t:F2}, цвет = {color}");
                }
            }
            catch (System.Exception e)
            {
                if (debugOverlay) Debug.LogError($"[HoverHL] PulseOverlay ошибка: {e.Message}");
                // Если объект уничтожен, выходим из корутины
                break;
            }
            
            yield return new WaitForSeconds(0.016f);
        }
        
        if (debugOverlay) Debug.Log("[HoverHL] PulseOverlay завершена");
    }
}


