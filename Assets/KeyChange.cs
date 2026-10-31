using UnityEngine;
using UnityEngine.UI;

public class KeyChange : MonoBehaviour
{
    public Image spriteRenderer; // ссылка на компонент SpriteRenderer

    public Sprite sprite1, sprite2, sprite3, sprite4, sprite5; // ссылка на первый спрайт

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        SwitchSprites(1); // по умолчанию ставим первый спрайт
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void SwitchSprites(int SpriteNumber)
    {
        switch (SpriteNumber) 
        {
            case 1: spriteRenderer.sprite = sprite1; break;
            case 2: spriteRenderer.sprite = sprite2; break;
            case 3: spriteRenderer.sprite = sprite3; break;
            case 4: spriteRenderer.sprite = sprite4; break;
            case 5: spriteRenderer.sprite = sprite5; break;
            default: spriteRenderer.sprite = sprite1; break;
        }
    }
}
