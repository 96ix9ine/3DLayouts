using UnityEngine;
using UnityEngine.UI;

public class ExitController : MonoBehaviour
{
    public Button exitButton;

    void Start()
    {
        exitButton.onClick.AddListener(QuitApplication);
    }

    void QuitApplication()
    {
        Application.Quit();
    }
}
