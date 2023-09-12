using System;
using System.Linq;
using Sandbox;

namespace Plates;

public class BigPlateRoundType : PlatesRound
{
    public BigPlateRoundType(){
        name = "Big Plate";
        command = "round_big_plate";
        description = "A single plate that fills nearly the entire arena";
    }

    public override void OnEvent(){
        foreach(var plate in Entity.All.OfType<Plate>()){
            plate.Delete();
        }
        var bigPlate = new Plate(Vector3.Zero, 1, "Nobody");
        bigPlate.SetSize(11f);

        Random Rand = new();
        foreach(var client in PlatesGame.Current.GameClients)
        {
            client.Pawn.Position = new Vector3(Rand.Float(-300f,300f), Rand.Float(-300f,300f), 150f);
        }
    }
}