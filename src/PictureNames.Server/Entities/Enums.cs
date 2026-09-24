namespace PictureNames.Server.Entities;

public enum RoomState
{
    Lobby,      // игроки собираются
    InGame,     // партия идёт
    Finished    // партия закончена, можно реванш
}

public enum TeamColor
{
    Blue,
    Red
}

public enum CardColor
{
    Blue,       // карта синей команды
    Red,        // карта красной команды
    Neutral,    // нейтральная
    Assassin    // ассасин — мгновенный проигрыш
}

public enum PlayerRole
{
    Spymaster,  // видит цвета всех карт
    Operative,  // угадывает, видит только открытые
    Spectator   // наблюдает
}

public enum MoveType
{
    Clue,       // спаймастер дал подсказку
    Guess,      // оперативник открыл карту
    EndTurn,    // ход завершён досрочно
    Join,
    Leave,
    GameStart,
    GameEnd
}