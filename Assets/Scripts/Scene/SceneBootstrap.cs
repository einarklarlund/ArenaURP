using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneBootstrap : MonoBehaviour
{
    [SerializeField] private string sceneName;
    void Start()
    {
        SceneManager.LoadScene(sceneName);
    }
}
