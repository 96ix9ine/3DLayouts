using UnityEngine;
using UnityEngine.UI;

public class ModelMenuController : MonoBehaviour
{
    [Header("UI Panels")]
    public GameObject mainMenuPanel;
    public GameObject viewerPanel;
    
    [Header("Model Data")]
    public ModelData[] models = new ModelData[6]; // Массив для хранения данных о моделях
    
    [Header("Model Controller")]
    public ModelController modelController;
    
    [Header("Buttons")]
    public Button[] menuButtons = new Button[6];
    
    void Start()
    {
        SetupButtons();
        SetupModelController();
    }
    
    void SetupButtons()
    {
        for (int i = 0; i < menuButtons.Length && i < models.Length; i++)
        {
            int buttonIndex = i; // Замыкание для корректной работы
            menuButtons[i].onClick.AddListener(() => OnModelButtonClicked(buttonIndex));
            
            // Устанавливаем текст кнопки
            Text buttonText = menuButtons[i].GetComponentInChildren<Text>();
            if (buttonText != null && models[i] != null)
            {
                buttonText.text = models[i].modelName;
            }
        }
    }
    
    void SetupModelController()
    {
        if (modelController == null)
            modelController = FindFirstObjectByType<ModelController>();
            
        if (modelController != null)
        {
            modelController.mainMenuPanel = mainMenuPanel;
            modelController.viewerPanel = viewerPanel;
        }
    }
    
    void OnModelButtonClicked(int modelIndex)
    {
        if (modelIndex >= 0 && modelIndex < models.Length)
        {
            ModelData selectedModel = models[modelIndex];
            if (modelController != null)
            {
                modelController.LoadModel(selectedModel);
            }
            else
            {
                Debug.LogError("ModelController не найден!");
            }
        }
    }
    
    // Метод для программной загрузки модели
    public void LoadModel(int modelIndex)
    {
        OnModelButtonClicked(modelIndex);
    }
    
    // Метод для добавления новой модели
    public void AddModel(int index, ModelData modelData)
    {
        if (index >= 0 && index < models.Length)
        {
            models[index] = modelData;
            
            // Обновляем текст кнопки
            if (menuButtons[index] != null)
            {
                Text buttonText = menuButtons[index].GetComponentInChildren<Text>();
                if (buttonText != null)
                {
                    buttonText.text = modelData.modelName;
                }
            }
        }
    }
    
    // Метод для возврата в главное меню
    public void ReturnToMainMenu()
    {
        if (viewerPanel != null)
            viewerPanel.SetActive(false);
            
        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(true);
    }
    
    // Метод для автоматической настройки моделей из папки Models
    [ContextMenu("Auto Setup Models")]
    public void AutoSetupModels()
    {
        // Ищем все FBX файлы в папке Models
        var modelPrefabs = Resources.LoadAll<GameObject>("Models");
        
        for (int i = 0; i < models.Length && i < modelPrefabs.Length; i++)
        {
            if (models[i] == null)
                models[i] = new ModelData();
                
            models[i].modelName = modelPrefabs[i].name;
            models[i].modelPrefab = modelPrefabs[i];
            
            // Обновляем текст кнопки
            if (menuButtons[i] != null)
            {
                Text buttonText = menuButtons[i].GetComponentInChildren<Text>();
                if (buttonText != null)
                {
                    buttonText.text = models[i].modelName;
                }
            }
        }
        
        Debug.Log($"Автоматически настроено {modelPrefabs.Length} моделей");
    }
}
