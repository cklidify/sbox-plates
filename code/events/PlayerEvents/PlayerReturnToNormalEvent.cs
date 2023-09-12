using Sandbox;

namespace Plates;

 
public class PlayerReturnToNormalEvent : PlatesEvent
{
    public PlayerReturnToNormalEvent(){
        name = "Player Returns To Normal";
        command = "player_return_normal";
        text = " player(s) will return to normal in ";
        type = EventType.Player;
        rarity = EventRarity.Rare;
    }

    public override void OnEvent(Entity ent){
        var ply = (ent as Player);
        var pos = ply.Position;
        ply.Respawn();
		ply.Position = pos;
        ply.InGame = true;
    }
}