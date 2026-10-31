using UnityEngine;

public class GameManager : MonoBehaviour
{
    public enum GameState
    { 
        Gameplay,
        Paused,
        Gameover
    }

    public GameState previousState;
    public GameState currentState;
    public GameObject pauseScreen;
    public GameObject gameOverScreen;

    void Awake()
    {
        Time.timeScale = 1f;
    }

    void Update()
    {
        switch (currentState)
        {
            case GameState.Gameplay:
                CheckForPauseAndResume();
                break;
            case GameState.Paused:
                CheckForPauseAndResume();
                break;
            case GameState.Gameover:
                break;
            default:
                Debug.LogError("Невідомий статус гри: " + currentState);
                break;
        }
    }

    public void ChangeState(GameState newState)
    {
        previousState = currentState;
        currentState = newState;
    }

    public void PauseGame()
    {
        if (currentState != GameState.Paused)
        {
            ChangeState(GameState.Paused);
            Time.timeScale = 0f;
            pauseScreen.SetActive(true);
        }
    }

    public void ResumeGame()
    {
        if (currentState == GameState.Paused)
        {
            ChangeState(previousState);
            Time.timeScale = 1f;
            pauseScreen.SetActive(false);
        }
    }

    void CheckForPauseAndResume()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (currentState == GameState.Paused)
            {
                ResumeGame();
            }
            else if (currentState == GameState.Gameplay)
            {
                PauseGame();
            }
        }
    }

    public void GameOver()
    {
        ChangeState(GameState.Gameover);
        Time.timeScale = 0f;
        gameOverScreen.SetActive(true);
    }
}
