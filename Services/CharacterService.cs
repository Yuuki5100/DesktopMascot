namespace DesktopMascot.Services;

public sealed class CharacterService
{
    public CharacterType CurrentCharacter { get; private set; } = CharacterType.Pompompurin;

    public bool SetCharacter(CharacterType character)
    {
        if (CurrentCharacter == character)
        {
            return false;
        }

        CurrentCharacter = character;
        return true;
    }
}
