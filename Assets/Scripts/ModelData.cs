using UnityEngine;

[System.Serializable]
public class ModelData
{
    [Header("Basic Info")]
    public string modelName;
    public GameObject modelPrefab; // FBX модель как префаб
    public Sprite previewImage;
    
    [Header("Model Settings")]
    public Vector3 initialPosition = Vector3.zero;
    public Vector3 initialRotation = Vector3.zero;
    public Vector3 initialScale = Vector3.one;
    
    [Header("Camera Settings")]
    public Vector3 cameraPosition = new Vector3(0, 0, -5);
    public Vector3 cameraRotation = Vector3.zero;
    public float cameraFOV = 60f;
    
    [System.Serializable]
    public class PartTooltipEntry
    {
        public string partName;
        [TextArea]
        public string tooltip;
    }

    [Header("Hover Tooltips (Name -> Text)")]
    public PartTooltipEntry[] partTooltips; // Соответствие имён объектов внутри префаба и текста подсказки
}
