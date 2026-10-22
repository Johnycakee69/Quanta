using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviour
{
    public void LoadTestMap()
    {
        SceneManager.LoadScene("PhotoelectricWorld");
    }
}