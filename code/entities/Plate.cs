using System;

namespace Sandbox
{
	[Library( "ent_plate", Title = "Plate", Spawnable = true)]
    public partial class Plate : Prop
	{

		[Net] public ulong owner {get;set;}
		[Net] public string ownerName {get;set;}

		[Net] public float toScale {get;set;} = 1;

		public Plate(){}

		public Plate(Vector3 pos, float size, ulong own, string name){
			Tags.Add("plate");
			Position = pos;
			owner = own;
			ownerName = name;
			Scale = size;
			toScale = size;
		}

		public override void Spawn(){
			base.Spawn();

			SetModel("models/plate.vmdl");
			SetupPhysicsFromModel(PhysicsMotionType.Static);

			// toPos = Position;
			// toScale = Scale;
		}

		[Event.Tick]
		public void Tick(){
			if(IsServer){
				Scale = MathC.Lerp(Scale,toScale,0.125f);
				DebugOverlay.Box(1,new Vector3(0,0,0), new Vector3(200,200,200));
			}
			if(toScale <= 0) Kill();
		}

		public void Kill(){
			MoveTo(Vector3.Up,1);
			DeleteAsync(0.1f);
		}

	}
}