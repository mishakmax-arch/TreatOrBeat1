using UnityEngine;

/// <summary>
/// Вешается на игрока (на объект со SpriteRenderer).
/// Содержит 5 публичных функций, каждая ставит свой спрайт.
/// Вызов из другого скрипта: playerSprite.SetSprite1();
/// </summary>
public class PlayerSpriteChanger : MonoBehaviour
{
    [Header("Спрайты (перетащите в Inspector)")]
    public Sprite sprite1;
    public Sprite sprite2;
    public Sprite sprite3;
    public Sprite sprite4;
    public Sprite sprite5;

    [Header("SpriteRenderer (если пусто - берётся с этого объекта)")]
    public SpriteRenderer target;

    void Awake()
    {
        if (target == null) target = GetComponent<SpriteRenderer>();
        if (target == null) target = GetComponentInChildren<SpriteRenderer>();
        if (target == null) Debug.LogWarning("PlayerSpriteChanger: SpriteRenderer не найден на " + name);
        SetSprite1(); // по умолчанию ставим первый спрайт
    }

    public void SetSprite1() { Apply(sprite1); }
    public void SetSprite2() { Apply(sprite2); }
    public void SetSprite3() { Apply(sprite3); }
    public void SetSprite4() { Apply(sprite4); }
    public void SetSprite5() { Apply(sprite5); }

    public void SetSprite(int x) 
    {
        switch (x) 
        { 
            case 1: SetSprite1(); break;
            case 2: SetSprite2(); break;
            case 3: SetSprite3(); break;
            case 4: SetSprite4(); break;
            case 5: SetSprite5(); break;

        }
    
    
    }



    void Apply(Sprite s)
    {
        if (target == null || s == null) return;
        target.sprite = s;
    }
}