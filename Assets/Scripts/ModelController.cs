using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;
using System.Collections.Generic;

public class ModelController : MonoBehaviour
{
    [Header("UI Panels")]
    public GameObject mainMenuPanel;
    public GameObject viewerPanel;
    
    [Header("3D Model Display")]
    public GameObject modelContainer; // Контейнер для 3D объектов в сцене
    public Camera playerCamera; // Камера для отображения модели
    
    [Header("Model Control UI")]
    public Button resetButton;
    public Button closeButton;
    
    [Header("Tooltip System")]
    public GameObject tooltipContainerPrefab;
    public bool useHoverTooltips = true; // новая система ховеров

    // 3D Model objects
    private GameObject currentModelObject;
    private Camera currentCamera;
    private GameObject tooltipContainerInstance;
    private TMPro.TextMeshProUGUI tooltipTextComponent;

    private HoverTooltipHighlighter currentHoverHighlighter;
    
    // Model transformation
    private Vector3 modelInitialPosition;
    private Vector3 modelInitialRotation;
    private Vector3 modelInitialScale;
    private Vector3 cameraInitialPosition;
    private Vector3 cameraInitialRotation;
    private float cameraInitialFOV;
    
    // Mouse/Touch control
    private float rotationSpeed = 2000f; // скорость вращения (град/пиксель/сек)
    private float zoomSpeed = 1.0f; // скорость зума
    private float panSpeed = 0.015f; // базовая скорость перемещения камеры (правая кнопка)
    
    // Camera orbit control
    private float cameraDistance = 2f;
    private Vector3 cameraOrbitAngles = Vector3.zero;
    private Vector3 modelCenter = Vector3.zero;
    
    // World-space bounds of current model (for camera collision)
    private Bounds modelWorldBounds;
    private bool modelBoundsValid = false;
    
    // Свойство для доступа к cameraDistance из других скриптов
    public float CurrentCameraDistance => cameraDistance;
    
    [Header("Camera Limits")]
    public float minZoomDistance = 0.7f;   // минимальная дистанция до центра (эквивалент Z ≈ -0,7 при yaw=0)
    public float maxZoomDistance = 1.5f;   // максимальная дистанция до центра (эквивалент Z ≈ -1.5 при yaw=0)
    
    [Header("Orbit Camera")]
    public bool useOrbitCamera = true; // использовать орбитальную камеру
    public float orbitRadius = 2.0f; // радиус орбиты камеры
    public LayerMask orbitLayerMask = -1; // слой для орбитального движения
    
    [Header("Camera Collision")]
    public float minSurfaceClearance = 1f; // минимальный зазор от поверхности модели
    public float collisionPushSpeed = 15f;    // скорость плавного отталкивания камеры

    // Текущий зазор (с учётом override для конкретной модели)
    private float currentMinSurfaceClearance;

    // Переопределения зазора по имени префаба (без суффикса (Clone))
    private readonly Dictionary<string, float> clearanceOverrides = new Dictionary<string, float>
    {
        { "Solenoid_Valve", 0.7f },
        { "Sprinkler_pump_vertical", 2f },
        { "Liquid_flow_indicator", 0.7f },
    };
    
    void Start()
    {
        // Находим камеру
        currentCamera = Camera.main;
        if (currentCamera == null)
        {
            currentCamera = FindAnyObjectByType<Camera>();
        }
        
        if (currentCamera == null)
        {
            Debug.LogError("Камера не найдена в сцене!");
            return;
        }
        
        // Инициализируем начальные параметры камеры
        cameraInitialPosition = currentCamera.transform.position;
        cameraInitialRotation = currentCamera.transform.eulerAngles;
        cameraInitialFOV = currentCamera.fieldOfView;
        
        // Вычисляем начальную дистанцию до центра
        cameraDistance = Vector3.Distance(currentCamera.transform.position, Vector3.zero);
        cameraDistance = Mathf.Clamp(cameraDistance, minZoomDistance, maxZoomDistance);
        
        // Инициализируем центр модели
        modelCenter = Vector3.zero;
        modelInitialPosition = Vector3.zero;

        // Текущий зазор по умолчанию
        currentMinSurfaceClearance = minSurfaceClearance;
        
        // Настраиваем UI и систему подсказок
        SetupUI();
        Setup3DModelDisplay();
        
        Debug.Log("ModelController инициализирован");
    }
    
    void SetupUI()
    {
        // Ищем UI элементы в ViewerPanel
        var viewerPanel = GameObject.Find("ViewerPanel");
        if (viewerPanel != null)
        {
            var buttonsContainer = viewerPanel.transform.Find("ButtonsContainer");
            if (buttonsContainer != null)
            {
                resetButton = buttonsContainer.Find("ResetButton")?.GetComponent<Button>();
                closeButton = buttonsContainer.Find("CloseButton")?.GetComponent<Button>();
            }
        }
        
        // Настраиваем кнопки
        if (resetButton != null)
            resetButton.onClick.AddListener(ResetModel);
        
        if (closeButton != null)
            closeButton.onClick.AddListener(CloseModel);
    }
    
    void Setup3DModelDisplay()
    {
        // Ищем ModelContainer как контейнер для 3D объектов
        if (modelContainer == null)
        {
            modelContainer = GameObject.Find("ModelContainer");
            if (modelContainer == null)
            {
                modelContainer = new GameObject("ModelContainer");
                Debug.Log("Создан ModelContainer");
            }
        }
        
        // Ищем или создаем камеру для модели
        if (playerCamera == null)
        {
            playerCamera = Camera.main;
            if (playerCamera == null)
            {
                var cameraObj = new GameObject("ModelCamera");
                playerCamera = cameraObj.AddComponent<Camera>();
                cameraObj.tag = "MainCamera";
                Debug.Log("Создана камера для модели");
            }
        }
        
        currentCamera = playerCamera;
        
        // Убеждаемся что камера настроена правильно
        if (currentCamera != null)
        {
            currentCamera.clearFlags = CameraClearFlags.SolidColor;
            currentCamera.backgroundColor = Color.gray;
            currentCamera.cullingMask = -1; // Рендерит все слои
        }
    }
    
    public void LoadModel(ModelData modelData)
    {
        if (modelData == null) return;
        
        Debug.Log($"Загружается модель: {modelData.modelName}");
        
        // Удаляем старый объект и его ховер
        if (currentModelObject != null)
        {
            currentHoverHighlighter = null;
            
            Destroy(currentModelObject);
            currentModelObject = null;
        }
        
        // Сбрасываем текст подсказки
        if (tooltipTextComponent != null)
        {
            tooltipTextComponent.text = "";
        }
        
        // Инстанцируем модель
        if (modelData.modelPrefab != null)
        {
            // Проверяем есть ли Canvas в иерархии
            var canvas = modelContainer?.GetComponentInParent<Canvas>();
            if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                // Если модель в ScreenSpaceOverlay Canvas, создаем её в корне сцены
                Debug.Log("Canvas в ScreenSpaceOverlay режиме, создаем модель в корне сцены");
                currentModelObject = Instantiate(modelData.modelPrefab);
                currentModelObject.transform.position = modelData.initialPosition;
                currentModelObject.transform.rotation = Quaternion.Euler(modelData.initialRotation);
                currentModelObject.transform.localScale = modelData.initialScale;
            }
            else
            {
                // Обычная загрузка в ModelContainer
                currentModelObject = Instantiate(modelData.modelPrefab, modelContainer?.transform);
                currentModelObject.transform.localPosition = modelData.initialPosition;
                currentModelObject.transform.localRotation = Quaternion.Euler(modelData.initialRotation);
                currentModelObject.transform.localScale = modelData.initialScale;
            }
            
            // Сохраняем начальные параметры
            modelInitialPosition = modelData.initialPosition;
            modelInitialRotation = modelData.initialRotation;
            modelInitialScale = modelData.initialScale;
            
            // Вычисляем и кешируем мировые границы модели
            var renderersForBounds = currentModelObject.GetComponentsInChildren<Renderer>();
            if (renderersForBounds != null && renderersForBounds.Length > 0)
            {
                modelWorldBounds = renderersForBounds[0].bounds;
                for (int i = 1; i < renderersForBounds.Length; i++)
                {
                    modelWorldBounds.Encapsulate(renderersForBounds[i].bounds);
                }
                modelBoundsValid = true;
                // Центр орбиты берем из реального центра модели
                modelCenter = modelWorldBounds.center;
            }
            else
            {
                modelBoundsValid = false;
                modelCenter = modelData.initialPosition;
            }

            // Применяем per-model override для зазора
            currentMinSurfaceClearance = minSurfaceClearance;
            string prefabName = currentModelObject.name;
            if (!string.IsNullOrEmpty(prefabName))
            {
                if (prefabName.EndsWith("(Clone)"))
                {
                    prefabName = prefabName.Substring(0, prefabName.Length - 7).Trim();
                }
                if (clearanceOverrides.TryGetValue(prefabName, out float overrideValue))
                {
                    currentMinSurfaceClearance = overrideValue;
                }
            }
            
            // Создаем орбитальный коллайдер
            CreateOrbitCollider();
            
            // Проверяем что модель активна и имеет рендереры
            if (currentModelObject != null)
            {
                var renderers = currentModelObject.GetComponentsInChildren<Renderer>();
                Debug.Log($"Модель {modelData.modelName} загружена. Рендереров: {renderers.Length}");
                
                foreach (var renderer in renderers)
                {
                    Debug.Log($"Рендерер: {renderer.name}, активен: {renderer.enabled}, видим: {renderer.isVisible}");
                }
            }
        }
        
        // Настраиваем камеру для орбитального движения
        if (currentCamera != null)
        {
            // Вычисляем начальное расстояние камеры
            cameraDistance = Vector3.Distance(modelData.cameraPosition, modelData.initialPosition);
            
            // Вычисляем начальные углы орбиты
            Vector3 directionToCamera = (modelData.cameraPosition - modelData.initialPosition).normalized;
            cameraOrbitAngles.x = Mathf.Asin(-directionToCamera.y) * Mathf.Rad2Deg;
            cameraOrbitAngles.y = Mathf.Atan2(directionToCamera.x, directionToCamera.z) * Mathf.Rad2Deg;
            
            // Устанавливаем позицию камеры
            UpdateCameraPosition();
            
            // Настраиваем FOV
            currentCamera.fieldOfView = modelData.cameraFOV;
            
            // Сохраняем начальные параметры камеры
            cameraInitialPosition = modelData.cameraPosition;
            cameraInitialRotation = modelData.cameraRotation;
            cameraInitialFOV = modelData.cameraFOV;
            
            Debug.Log($"Камера настроена для орбитального движения. Позиция: {currentCamera.transform.position}, смотрит на: {modelCenter}");
        }
        
        // Подключаем систему подсказок-ховера
        if (useHoverTooltips && currentModelObject != null)
        {
            currentHoverHighlighter = currentModelObject.AddComponent<HoverTooltipHighlighter>();
            if (tooltipContainerPrefab != null)
            {
                currentHoverHighlighter.tooltipContainerPrefab = tooltipContainerPrefab;
            }
            currentHoverHighlighter.Initialize(currentModelObject, modelData, currentCamera);
        }


        
        // Обновляем target для OrbitCameraDebug если есть
        UpdateOrbitDebugTarget();
        
        // Инициализация завершена
        
        SwitchToViewerPanel();
    }
    
    void UpdateOrbitDebugTarget()
    {
        // Ищем OrbitCameraDebug на камере и обновляем target
        if (currentCamera != null)
        {
            var orbitDebug = currentCamera.GetComponent<OrbitCameraDebug>();
            if (orbitDebug != null && currentModelObject != null)
            {
                orbitDebug.UpdateTarget(currentModelObject);
            }
        }
    }
    
    void CreateOrbitCollider()
    {
        if (!useOrbitCamera || currentModelObject == null) return;
        
        // Находим границы модели
        var renderers = currentModelObject.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        
        Bounds modelBounds = renderers[0].bounds;
        foreach (var renderer in renderers)
        {
            modelBounds.Encapsulate(renderer.bounds);
        }
        
        // Создаем сферический коллайдер
        GameObject orbitColliderObj = new GameObject("OrbitCollider");
        orbitColliderObj.transform.SetParent(currentModelObject.transform);
        orbitColliderObj.transform.position = modelBounds.center;
        
        SphereCollider orbitCollider = orbitColliderObj.AddComponent<SphereCollider>();
        float radius = Mathf.Max(modelBounds.extents.x, modelBounds.extents.y, modelBounds.extents.z) + orbitRadius;
        orbitCollider.radius = radius;
        orbitCollider.isTrigger = true;
        
        // Настраиваем слой для орбитального движения (исправляем ошибку с layer)
        if (orbitLayerMask.value != 0)
        {
            // Находим первый активный слой в маске
            for (int i = 0; i < 32; i++)
            {
                if (((1 << i) & orbitLayerMask.value) != 0)
                {
                    orbitColliderObj.layer = i;
                    break;
                }
            }
        }
        else
        {
            // Если маска не задана, используем слой по умолчанию
            orbitColliderObj.layer = 0;
        }
        
        Debug.Log($"Создан орбитальный коллайдер радиусом {radius} вокруг модели");
    }
    

    
    void UpdateCameraPosition()
    {
        if (currentCamera == null) return;
        
        // Вычисляем позицию камеры на орбите
        float xRad = cameraOrbitAngles.x * Mathf.Deg2Rad;
        float yRad = cameraOrbitAngles.y * Mathf.Deg2Rad;
        
        Vector3 cameraPosition = modelCenter;
        cameraPosition.x += cameraDistance * Mathf.Sin(yRad) * Mathf.Cos(xRad);
        cameraPosition.y += cameraDistance * Mathf.Sin(xRad);
        cameraPosition.z += cameraDistance * Mathf.Cos(yRad) * Mathf.Cos(xRad);
        
        // Коллизия камеры с моделью: мягко отталкиваем, если слишком близко/внутри
        if (modelBoundsValid)
        {
            Vector3 dir = (cameraPosition - modelCenter).normalized;
            if (dir.sqrMagnitude > 0.0001f)
            {
                Ray ray = new Ray(modelCenter, dir);
                float hitDistance;
                if (modelWorldBounds.IntersectRay(ray, out hitDistance))
                {
                    float requiredDistance = hitDistance + currentMinSurfaceClearance;
                    if (cameraDistance < requiredDistance)
                    {
                        float targetDistance = Mathf.Min(maxZoomDistance, requiredDistance);
                        cameraDistance = Mathf.Lerp(cameraDistance, targetDistance, Time.deltaTime * collisionPushSpeed);
                        // Пересчитываем позицию после изменения дистанции
                        cameraPosition = modelCenter;
                        cameraPosition.x += cameraDistance * Mathf.Sin(yRad) * Mathf.Cos(xRad);
                        cameraPosition.y += cameraDistance * Mathf.Sin(xRad);
                        cameraPosition.z += cameraDistance * Mathf.Cos(yRad) * Mathf.Cos(xRad);
                    }
                }
            }
        }
        
        currentCamera.transform.position = cameraPosition;
        currentCamera.transform.LookAt(modelCenter);
        
        // Отладочная информация
        // if (currentModelObject != null)
        // {
        //     Debug.Log($"Камера: {currentCamera.transform.position} -> {modelCenter}, " +
        //              $"Модель: {currentModelObject.transform.position}, " +
        //              $"Активна: {currentModelObject.activeInHierarchy}");
        // }
    }
    

    

    
    void Update()
    {
        HandleMouseInput();
        
        // Обновляем позицию камеры каждый кадр для плавности
        if (useOrbitCamera && currentCamera != null)
        {
            UpdateCameraPosition();
        }
    }
    
    void HandleMouseInput()
    {
        if (currentCamera == null) return;
        
        float speedMul = Input.GetKey(KeyCode.LeftShift) ? 2f : 1f;
        
        // Получаем deltaTime с учетом возможных проблем в билде
        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f || Time.timeScale <= 0f)
        {
            deltaTime = 0.016f; // Fallback на 60 FPS если deltaTime некорректный
        }
        
        // Вращение камеры вокруг модели (левая кнопка мыши)
        if (Input.GetMouseButton(0))
        {
            float mouseX = Input.GetAxis("Mouse X") * rotationSpeed * speedMul * deltaTime;
            float mouseY = Input.GetAxis("Mouse Y") * rotationSpeed * speedMul * deltaTime;
            
            // Обновляем углы орбиты
            cameraOrbitAngles.y += mouseX;
            cameraOrbitAngles.x -= mouseY;
            
            // Ограничиваем вертикальное вращение
            cameraOrbitAngles.x = Mathf.Clamp(cameraOrbitAngles.x, -89f, 89f);
        }
        
        // Зум камеры (колесико мыши)
        if (Input.GetAxis("Mouse ScrollWheel") != 0)
        {
            float scrollDelta = Input.GetAxis("Mouse ScrollWheel") * zoomSpeed * speedMul;
            cameraDistance = Mathf.Clamp(cameraDistance - scrollDelta, minZoomDistance, maxZoomDistance);
        }
        
        // Перемещение камеры (правая кнопка мыши)
        if (Input.GetMouseButton(1))
        {
            Vector2 delta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
            
            // Вычисляем направление движения относительно камеры
            Vector3 right = currentCamera.transform.right;
            Vector3 up = currentCamera.transform.up;
            
            // Скорость перемещения зависит от дистанции до модели
            float distanceFactor = Mathf.Lerp(0.5f, 2f, Mathf.InverseLerp(minZoomDistance, maxZoomDistance, cameraDistance));
            
            // Перемещаем центр модели (камера остается на орбите)
            modelCenter -= right * delta.x * panSpeed * speedMul * distanceFactor;
            modelCenter -= up * delta.y * panSpeed * speedMul * distanceFactor; // Инвертированный Y
        }
    }
    
    void UpdateCameraPosition(float? newYaw = null, float? newPitch = null)
    {
        if (currentCamera == null) return;
        
        // Обновляем углы орбиты если указаны новые
        if (newYaw.HasValue)
            cameraOrbitAngles.y = newYaw.Value;
        if (newPitch.HasValue)
            cameraOrbitAngles.x = newPitch.Value;
        
        // Вычисляем позицию камеры на орбите
        float xRad = cameraOrbitAngles.x * Mathf.Deg2Rad;
        float yRad = cameraOrbitAngles.y * Mathf.Deg2Rad;
        
        Vector3 cameraPosition = modelCenter;
        cameraPosition.x += cameraDistance * Mathf.Sin(yRad) * Mathf.Cos(xRad);
        cameraPosition.y += cameraDistance * Mathf.Sin(xRad);
        cameraPosition.z += cameraDistance * Mathf.Cos(yRad) * Mathf.Cos(xRad);
        
        // Коллизия камеры с моделью: жесткая коррекция при явном апдейте
        if (modelBoundsValid)
        {
            Vector3 dir = (cameraPosition - modelCenter).normalized;
            if (dir.sqrMagnitude > 0.0001f)
            {
                Ray ray = new Ray(modelCenter, dir);
                float hitDistance;
                if (modelWorldBounds.IntersectRay(ray, out hitDistance))
                {
                    float requiredDistance = hitDistance + currentMinSurfaceClearance;
                    if (cameraDistance < requiredDistance)
                    {
                        cameraDistance = Mathf.Min(maxZoomDistance, requiredDistance);
                        cameraPosition = modelCenter;
                        cameraPosition.x += cameraDistance * Mathf.Sin(yRad) * Mathf.Cos(xRad);
                        cameraPosition.y += cameraDistance * Mathf.Sin(xRad);
                        cameraPosition.z += cameraDistance * Mathf.Cos(yRad) * Mathf.Cos(xRad);
                    }
                }
            }
        }
        
        currentCamera.transform.position = cameraPosition;
        currentCamera.transform.LookAt(modelCenter);
    }
    

    
    public void ResetModel()
    {
        if (currentModelObject == null) return;
        
        // Сбрасываем центр модели
        modelCenter = modelInitialPosition;
        
        // Сбрасываем камеру в начальное положение
        if (currentCamera != null)
        {
            currentCamera.transform.position = cameraInitialPosition;
            currentCamera.transform.rotation = Quaternion.Euler(cameraInitialRotation);
            currentCamera.fieldOfView = cameraInitialFOV;
        }
        
        // Обновляем дистанцию камеры
        cameraDistance = Vector3.Distance(currentCamera.transform.position, modelCenter);
        cameraDistance = Mathf.Clamp(cameraDistance, minZoomDistance, maxZoomDistance);
        
        // Пересчитываем углы орбиты из текущего положения камеры, чтобы избежать скачка Y=180
        Vector3 directionToCamera = (currentCamera.transform.position - modelCenter).normalized;
        cameraOrbitAngles.x = Mathf.Asin(-directionToCamera.y) * Mathf.Rad2Deg;
        cameraOrbitAngles.y = Mathf.Atan2(directionToCamera.x, directionToCamera.z) * Mathf.Rad2Deg;
        
        // Применяем согласованное положение/поворот камеры относительно центра модели
        UpdateCameraPosition();
        
        Debug.Log("Модель сброшена в начальное положение");
    }
    
    public void CloseModel()
    {

        
        Debug.Log("Модель закрыта, все подсказки закрыты");
        SwitchToMainMenu();
    }
    
    void SwitchToViewerPanel()
    {
        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(false);
        
        if (viewerPanel != null)
            viewerPanel.SetActive(true);
            
        // Убеждаемся что камера активна и видна
        if (currentCamera != null)
        {
            currentCamera.gameObject.SetActive(true);
            Debug.Log($"Камера активирована: {currentCamera.name}, активна: {currentCamera.gameObject.activeInHierarchy}");
        }
        
        // Проверяем что модель видна
        if (currentModelObject != null)
        {
            Debug.Log($"Модель в иерархии: {currentModelObject.name}, активна: {currentModelObject.activeInHierarchy}");
            
            // Если модель в Canvas, убеждаемся что Canvas настроен правильно
            var canvas = currentModelObject.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                Debug.Log($"Модель в Canvas: {canvas.name}, режим: {canvas.renderMode}");
                if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    Debug.LogWarning("Модель в ScreenSpaceOverlay Canvas! Это может быть проблемой.");
                }
            }
        }
    }
    
    void SwitchToMainMenu()
    {
        if (viewerPanel != null)
            viewerPanel.SetActive(false);
        
        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(true);
    }
}
