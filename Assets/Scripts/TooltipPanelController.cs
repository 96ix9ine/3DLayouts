using UnityEngine;
using UnityEngine.UI;

public class TooltipPanelController : MonoBehaviour
{
	[Header("References")]
	public GameObject tooltipPanel;
	public Button TooltipButton;                 // Кнопка открыть
	public Button CloseTooltipPanelButton;       // Кнопка закрыть

	[Header("Options")]
	public bool hideOnStart = true;

	void Awake()
	{
		Unsubscribe();
		Subscribe();
		if (tooltipPanel != null && hideOnStart)
		{
			tooltipPanel.SetActive(false);
		}
	}

	void OnEnable()
	{
		Subscribe();
	}

	void OnDisable()
	{
		Unsubscribe();
	}

	void Subscribe()
	{
		if (TooltipButton != null) TooltipButton.onClick.AddListener(Show);
		if (CloseTooltipPanelButton != null) CloseTooltipPanelButton.onClick.AddListener(Hide);
	}

	void Unsubscribe()
	{
		if (TooltipButton != null) TooltipButton.onClick.RemoveListener(Show);
		if (CloseTooltipPanelButton != null) CloseTooltipPanelButton.onClick.RemoveListener(Hide);
	}

	[ContextMenu("Show Tooltip Panel")] 
	public void Show()
	{
		if (tooltipPanel != null)
		{
			tooltipPanel.SetActive(true);
		}
	}

	[ContextMenu("Hide Tooltip Panel")] 
	public void Hide()
	{
		if (tooltipPanel != null)
		{
			tooltipPanel.SetActive(false);
		}
	}

	[ContextMenu("Toggle Tooltip Panel")] 
	public void Toggle()
	{
		if (tooltipPanel != null)
		{
			tooltipPanel.SetActive(!tooltipPanel.activeSelf);
		}
	}
}


