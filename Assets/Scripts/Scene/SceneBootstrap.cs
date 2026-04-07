using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;


public class SceneBootstrap : MonoBehaviour
{
    public string sceneName;
    void Start()
    {
        SceneManager.LoadScene(sceneName);
    }
}
