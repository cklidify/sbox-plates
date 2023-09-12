using System;
using System.Linq;
using Sandbox;

namespace Plates;
 
public class BigPlatesRoundType : PlatesRound
{
    public BigPlatesRoundType(){
        name = "Big Plates";
        command = "round_big_plates";
        description = "All plates start at 1.5x scale";
    }

    public override void OnEvent(){
        foreach(var plate in Entity.All.OfType<Plate>()){
            plate.SetSize(1.5f);
        }
    }
}

 
public class RandomSizePlatesRoundType : PlatesRound
{
    public RandomSizePlatesRoundType(){
        name = "Random Sizes";
        command = "round_random_sizes";
        description = "Every plate has a random size";
    }

    public override void OnEvent(){
        Random Rand = new();
        foreach(var plate in Entity.All.OfType<Plate>()){
            var _scl = Rand.Float(0.2f, 2f);
            plate.SetSize(_scl);
        }
    }
}

 
public class MicroPlatesRoundType : PlatesRound
{
    public MicroPlatesRoundType(){
        name = "Micro Plates";
        command = "round_micro_plates";
        description = "All plates start at 0.2x scale";
    }

    public override void OnEvent(){
        foreach(var plate in Entity.All.OfType<Plate>()){
            plate.SetSize(0.2f);
        }
    }
}