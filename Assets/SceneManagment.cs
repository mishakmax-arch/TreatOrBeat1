using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneManagment : MonoBehaviour
{
    public void LoadScene(string sceneName)
    {
        ResumeTime();
        SceneManager.LoadScene(sceneName);
    }

    public void RestartScene()
    {
        ResumeTime();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        // в редакторе Application.Quit() ничего не делает - останавливаем режим Play
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // GameManager ставит Time.timeScale = 0 при смерти и паузе. timeScale - глобальная настройка,
    // она не сбрасывается сама при смене сцены, поэтому меню грузилось "замороженным".
    static void ResumeTime()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;
    }
}