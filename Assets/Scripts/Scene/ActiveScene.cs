using UnityEngine;
using UnityEngine.SceneManagement;

public class ActiveScene : MonoBehaviour
{
    void Start()
    {
        SceneManager.SetActiveScene(gameObject.scene);
    }
}
