using UnityEngine;

public abstract class View : MonoBehaviour
{   
    public virtual void Show()
    {
        Cursor.lockState = CursorLockMode.None;
        gameObject.SetActive(true);
    }

    public virtual void Hide() => gameObject.SetActive(false);
}