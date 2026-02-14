using UnityEngine;

public class OrbitCameraDebug : MonoBehaviour
{
    [Header("Debug Info")]
    public bool showDebugInfo = true;
    public bool showOrbitPath = true;
    
    [Header("Orbit Settings")]
    public Transform target;
    public float orbitRadius = 2f;
    public int orbitSegments = 32;
    
    [Header("Auto Setup")]
    public bool autoSetupOnStart = true;
    public bool findModelContainer = true;
    public bool findCamera = true;
    
    private Camera cam;
    private LineRenderer orbitLine;
    
    void Start()
    {
        cam = GetComponent<Camera>();
        if (cam == null)
        {
            cam = Camera.main;
        }
        
        if (autoSetupOnStart)
        {
            SetupOrbitDebug();
        }
        
        if (showOrbitPath)
        {
            CreateOrbitPath();
        }
    }
    
    void SetupOrbitDebug()
    {
        // Настраиваем target
        if (findModelContainer)
        {
            // Ищем ModelContainer в корне сцены (не внутри Canvas)
            GameObject modelContainer = GameObject.Find("ModelContainer");
            if (modelContainer != null)
            {
                // Проверяем, что ModelContainer НЕ находится внутри Canvas
                var canvas = modelContainer.GetComponentInParent<Canvas>();
                if (canvas == null)
                {
                    target = modelContainer.transform;
                    Debug.Log($"OrbitCameraDebug: target установлен в ModelContainer (корень сцены)");
                }
                else
                {
                    Debug.LogWarning("ModelContainer находится внутри Canvas! OrbitCameraDebug не будет работать корректно.");
                }
            }
            else
            {
                Debug.LogWarning("ModelContainer не найден! Создайте его вручную или загрузите модель.");
            }
        }
        
        // Настраиваем камеру
        if (findCamera)
        {
            if (cam != null)
            {
                orbitRadius = 2f; // базовый радиус
                Debug.Log($"OrbitCameraDebug: камера найдена, радиус орбиты = {orbitRadius}");
            }
        }
    }
    
    // Публичный метод для ручной настройки
    public void SetupManually()
    {
        SetupOrbitDebug();
    }
    
    // Метод для обновления target после загрузки модели
    public void UpdateTarget(GameObject newTarget)
    {
        if (newTarget != null)
        {
            target = newTarget.transform;
            Debug.Log($"OrbitCameraDebug: target обновлен на {newTarget.name}");
        }
    }
    
    void CreateOrbitPath()
    {
        GameObject orbitPathObj = new GameObject("OrbitPath");
        orbitPathObj.transform.SetParent(transform);
        
        orbitLine = orbitPathObj.AddComponent<LineRenderer>();
        orbitLine.useWorldSpace = true;
        orbitLine.positionCount = orbitSegments + 1;
        orbitLine.startWidth = 0.01f;
        orbitLine.endWidth = 0.01f;
        orbitLine.material = new Material(Shader.Find("Sprites/Default"));
        orbitLine.material.color = Color.cyan;
        orbitLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        orbitLine.receiveShadows = false;
        
        UpdateOrbitPath();
    }
    
    void UpdateOrbitPath()
    {
        if (orbitLine == null || target == null) return;
        
        Vector3 center = target.position;
        
        for (int i = 0; i <= orbitSegments; i++)
        {
            float angle = (float)i / orbitSegments * 360f * Mathf.Deg2Rad;
            float x = center.x + orbitRadius * Mathf.Sin(angle);
            float z = center.z + orbitRadius * Mathf.Cos(angle);
            float y = center.y;
            
            orbitLine.SetPosition(i, new Vector3(x, y, z));
        }
    }
    
    void OnGUI()
    {
        if (!showDebugInfo || cam == null) return;
        
        GUILayout.BeginArea(new Rect(10, 10, 300, 200));
        GUILayout.Label("Orbit Camera Debug", GUI.skin.box);
        
        if (target != null)
        {
            GUILayout.Label($"Target: {target.name}");
            GUILayout.Label($"Target Position: {target.position}");
        }
        
        GUILayout.Label($"Camera Position: {cam.transform.position}");
        GUILayout.Label($"Camera Rotation: {cam.transform.eulerAngles}");
        
        Vector3 toTarget = target != null ? (target.position - cam.transform.position) : Vector3.zero;
        GUILayout.Label($"Distance to Target: {toTarget.magnitude:F2}");
        
        GUILayout.EndArea();
    }
    
    void Update()
    {
        if (showOrbitPath && orbitLine != null && target != null)
        {
            UpdateOrbitPath();
        }
    }
}
