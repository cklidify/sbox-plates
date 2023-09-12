﻿using Sandbox;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;

namespace Plates;

public enum PlatesGameState {NOT_ENOUGH_PLAYERS = -2, GAME_OVER = -1, STARTING_SOON = 0, SELECTING_EVENT = 1, PERFORMING_EVENT = 2}

public partial class PlatesGame : GameManager
{
	public static new PlatesGame Current {get;set;}
	public static PlatesGameState GameState {get;set;} = PlatesGameState.STARTING_SOON;

	// Game-related variables
	[Net] public RealTimeSince GameTimer {get;set;} = -30f;
	[Net] public RealTimeSince GameLength {get;set;} = 0f;
	[Net] public float LastTimer {get;set;} = -10f;
	[Net] public IList<IClient> GameClients {get;set;} = new();
	[Net] public IList<LossInformation> Eliminated {get;set;} = new();
	[Net] public IList<Entity> GameEntities {get;set;} = new();
	[Net] public int StartingPlayerCount {get;set;} = 1;
	[Net] public PlatesRound GameRound {get;set;}
	[Net] public int AffectedPlayers {get;set;} = 0;
	[Net] public int TotalAffectedPlayers {get;set;} = 0;
	
	// Console variables
	[ConVar.Replicated("plates_round_timer", Help = "Set the time between rounds in seconds")]
	public static float TimeBetweenRounds {get;set;} = 10f;
	[ConVar.Replicated("plates_minimum_players", Help = "Set the minimum required players to start a round")]
	public static int MinimumRequiredPlayers {get;set;} = 2;

	// Networked text
	[Net] public string EventText {get;set;} = "";
	[Net] public string EventSubtext {get;set;} = "";

	public PlatesGame()
	{
		Current = this;

		if(Game.IsServer)
		{
			// Create the HUD Instance
			_ = new PlatesHud();

			// Load the game events
			LoadEvents();
		}
	}

	/// <summary>
	/// A client joined the server. Give them a player to play with
	/// </summary>
	public override void ClientJoined( IClient client )
	{
		base.ClientJoined( client );

		// Create a player for this client to play with
		var player = new Player( client );
		player.Respawn();
		client.Pawn = player;

		// Request Player Data
		PlayerDataManager.AddTempEntry( client.SteamId );
		PlayerDataManager.RequestPlayerData(To.Single(client));

		// Set client variables
		//GetClientRank(client);
	}
	/// <summary>
	/// A client is killed. Eliminate them if they were in-game
	/// </summary>
	public override void OnKilled( IClient client, Entity pawn )
	{
		base.OnKilled(client, pawn);

		if(!Game.IsServer) return;
		
		foreach(var plate in Entity.All.OfType<Plate>())
		{
			if(plate.owner == client)
			{
				plate.Kill();
				break;
			}
		}

		// Remove client from game clients list
		SetLose(client);
		if(GameClients.Remove(client)) RequestGamePlayersForScreen();
	}

	[GameEvent.Tick.Server]
	public void ServerTick()
	{
		switch(GameState)
		{
			case PlatesGameState.NOT_ENOUGH_PLAYERS:
				EventText = "Not Enough Players!";
				EventSubtext = "Type plates_start in console to start anyway";
				if(GameTimer >= 0)
				{
					GameState = PlatesGameState.STARTING_SOON;
					GameTimer = -TimeBetweenRounds;
				}
				break;
			case PlatesGameState.GAME_OVER:
				EventText = "Game Over! Winner: " + GetWinner().Name;
				if(GameTimer >= 0)
				{
					for(var i=0; i<GameClients.Count; i++)
					{
						(GameClients[i].Pawn as Player)?.Respawn();
					}
					GameTimer = -TimeBetweenRounds;
					GameState = PlatesGameState.STARTING_SOON;
				}
				break;
			case PlatesGameState.STARTING_SOON:
				EventText = "Game Starting in " + MathF.Ceiling(-GameTimer).ToString() + "s";
				EventSubtext = "";
				if(GameTimer >= 0)
				{
					if(Game.Clients.Count >= MinimumRequiredPlayers)
					{
						StartGame();
					}
					else
					{
						GameTimer = -8f;
						GameState = PlatesGameState.NOT_ENOUGH_PLAYERS;
					}
				}
				break;
			default:
				var prevTime = GameTimer;
				if(GameState == PlatesGameState.SELECTING_EVENT)
				{
					if(GameTimer <= 0f && Math.Floor(GameTimer) > LastTimer)
					{
						Sound.FromScreen("plates_timer");
						LastTimer = (float)Math.Floor(GameTimer);
					}
					var str = TotalAffectedPlayers + CurrentEvent.text;
					if(CurrentEvent.type == EventType.Arena) str = CurrentEvent.text;
					if(GameTimer >= 0) EventText = str + "0s";
					else EventText = str + MathF.Ceiling(-GameTimer).ToString() + "s";
				}
				if(GameTimer >= 0)
				{
					if(AffectedPlayers <= 0) GetNextEvent();
					else PerformEvent();
				}
				break;
		}

		HeaderText.updateText(EventText, EventSubtext);
	}

	/// <summary>
	/// Starts a game of Plates assuming one isn't already in progress
	/// </summary>
	[ConCmd.Admin("plates_start", Help = "Forces the game to start if one isn't already active")]
	public static void StartGame()
	{
		// If game is already active, do nothing
		if((int)GameState > (int)PlatesGameState.STARTING_SOON) return;

		// Start keeping track of game length
		Current.GameLength = 0f;

		// Respawn all players
		RespawnAllPlayers();

		// Clear all entries from the previous Round Report
		RoundReport.ClearEntries();

		// Add clients to the GameClients list
		Current.GameClients = new List<IClient>();
		Current.Eliminated = new List<LossInformation>();
		foreach(var client in Game.Clients)
		{
			Current.GameClients.Add(client);
			//GetClientRank(client);
		}

		// Keep track of how many players we started with
		Current.StartingPlayerCount = Game.Clients.Count;

		// Initialize plates and assign a plate to each player
		InitPlates();
		AssignPlates();

		// Fill the round queue if there aren't enough
		FillQueue(5);

		// Get the oldest round in the queue
		Current.GameRound = RoundQueue[0];
		Current.GameRound.OnEvent();
		RoundQueue.RemoveAt(0);
		
		RequestGamePlayersForScreen();
		RoundQueueScreen.RemoveLatest();
		RoundInfo.SetRoundText(Current.GameRound.name, Current.GameRound.description);

		Current.GetNextEvent();
	}

	/// <summary>
	/// Ends a game of Plates assuming one is already in progress
	/// </summary>
	[ConCmd.Admin("plates_end", Help = "Forces the game to end if one is active")]
	public static void EndGame()
	{
		// If game isn't active, do nothing
		if((int)GameState <= (int)PlatesGameState.STARTING_SOON) return;

		// Make sure winners are set proper
		foreach(var client in Current.GameClients)
		{
			Current.SetLose(client);
		}

		// Set winners podiums
		foreach(var podium in Entity.All.OfType<WinnersPodium>().ToList())
		{
			if(podium.IsValid() && podium.WinPosition <= Current.Eliminated.Count){
				podium.Dress(Current.Eliminated[Current.Eliminated.Count - podium.WinPosition].client);
			}
		}

		// Play Round End Music
		Random Rand = new();
		var _r = Rand.Int(1, 9);
		Sound.FromScreen("plates_round_end_" + _r);

		// Show Round Report UI
		RoundReport.Show();

		foreach(var cl in Current.GameClients)
		{
			(cl.Pawn as Player).Respawn();
		}
		Current.GameClients = new List<IClient>();
		foreach(var plate in Entity.All.OfType<Plate>()) plate.Delete();
		foreach(var ent in Current.GameEntities)
		{
			if(ent.IsValid()) ent.Delete();
		}
		Current.GameEntities = new List<Entity>();
		Current.GameTimer = -10;
		GameState = PlatesGameState.GAME_OVER;

		CurrentGameScreen.ClearList();
	}

	// public static async void GetClientRank(IClient client)
	// {
	// 	//var gameRank = await client.FetchGameRankAsync();
	// 	var http = new Sandbox.Internal.Http(new Uri("https://sap.facepunch.com/asset/carsonk.plates/rank/" + client.PlayerId));
    //     var response = await http.GetStringAsync();
	// 	var gameRank = Json.Deserialize<PlayerGameRank>(response);
	// 	client.SetInt("wins", gameRank.Wins);
	// 	client.SetInt("rank", gameRank.Global.Position);
	// }

	/// <summary>
	/// Spawn a grid of plates
	/// </summary>
	public static void InitPlates()
	{
		for(var i=-4; i<4; i++){
			for(var j=-4; j<4; j++){
				Plate plate = new Plate(new Vector3((i+0.5f)*92*4,(j+0.5f)*92*4,0), 1, "Nobody");
			}
		}
	}

	/// <summary>
	/// Assigns a plate to each player and destroys the excess
	/// </summary>
	public static void AssignPlates()
	{
		var _playerCount = Game.Clients.Count;
		var _curPlayer = 0;

		Random Rand = new();

		foreach(var plate in Entity.All.OfType<Plate>().OrderBy(x => Rand.Double(0f, 1f)))
		{
			if(_curPlayer >= _playerCount)
			{
				plate.Delete();
			}
			else
			{
				var client = Game.Clients.ElementAt(_curPlayer);
				plate.owner = client;
				plate.ownerName = plate.owner.Name;
				if(client.Pawn is Player ply)
				{
					ply.CurrentPlate = plate;
					ply.InGame = true;
					ply.Position = plate.Position + Vector3.Up * 100.0f;
					ply.BaseVelocity = Vector3.Zero;
					ply.Velocity = Vector3.Zero;
				}
			}
			_curPlayer++;
		}
	}

	public void AddEntity(Entity ent)
	{
		GameEntities.Add(ent);
	}

	public IClient GetWinner()
	{
		return Eliminated[Eliminated.Count-1].client;
	}

	public void SetLose(IClient client)
	{
		if(GameClients.Contains(client)){
			var _loss = new LossInformation(client, GameLength, (client.Pawn as Player).EventCount, GameClients.Count);
			Eliminated.Add(_loss);
			RoundReport.AddEntry(_loss.position, _loss.client.SteamId, _loss.client.Name, _loss.timeAlive, _loss.eventCount);
		}
	}

	public void ResetGlows()
	{
		foreach(var plate in Entity.All.OfType<Plate>())
		{
			plate.SetGlow(false);
		}
		foreach(var client in Game.Clients)
		{
			if(client.Pawn is Player player)
			{
				player.SetGlow(false);
			}
		}
	}

	public static void Explosion( Entity owner, Vector3 position, float radius, float damage, float forceScale )
	{
		// Effects
		Sound.FromWorld( "rust_pumpshotgun.shootdouble", position );
		Particles.Create( "particles/explosion/barrel_explosion/explosion_barrel.vpcf", position );

		// Damage, etc
		var overlaps = Entity.FindInSphere( position, radius );

		foreach ( var overlap in overlaps )
		{
			if ( overlap is not ModelEntity ent || !ent.IsValid() )
				continue;

			if ( ent.LifeState != LifeState.Alive )
				continue;

			if ( !ent.PhysicsBody.IsValid() )
				continue;

			if ( ent.IsWorld )
				continue;

			var targetPos = ent.PhysicsBody.MassCenter;

			var dist = Vector3.DistanceBetween( position, targetPos );
			if ( dist > radius )
				continue;

			var tr = Trace.Ray( position, targetPos )
				.WorldOnly()
				.Run();

			if ( tr.Fraction < 0.98f )
				continue;

			var distanceMul = 1.0f - Math.Clamp( dist / radius, 0.0f, 1.0f );
			var dmg = damage * distanceMul;
			var force = (forceScale * distanceMul) * ent.PhysicsBody.Mass;
			var forceDir = (targetPos - position).Normal;

			var damageInfo = DamageInfo.FromExplosion( position, forceDir * force, dmg )
				.WithAttacker( owner );

			ent.TakeDamage( damageInfo );
		}
	}

	[ConCmd.Server]
	public static void RequestGamePlayersForScreen()
	{
		List<long> ingameIds = new();
		List<string> ingameNames = new();
		foreach(var cl in Current.GameClients)
		{
			ingameIds.Add(cl.SteamId);
			ingameNames.Add(cl.Name);
		}
		// foreach(var el in Eliminated)
		// {
		// 	eliminated.Add(el.client.PlayerId, el.client.Name);
		// }
		CurrentGameScreen.Populate(ingameIds.ToArray(), ingameNames.ToArray());
	}

	[ConCmd.Server]
	public static void RequestRoundQueueForScreen()
	{
		List<string> rounds = new();
		foreach(var round in RoundQueue)
		{
			rounds.Add(round.name);
		}
		RoundQueueScreen.Populate(rounds.ToArray());
	}

	/// <summary>
	/// Respawn all connected clients
	/// </summary>
	public static void RespawnAllPlayers()
	{
		foreach(var client in Game.Clients)
		{
			if(client.Pawn is not Player player) continue;
			player.Respawn();
		}
	}

	/// <summary>
	/// Should we send voice data to this player
	/// </summary>
	public override bool CanHearPlayerVoice( IClient source, IClient dest )
	{
		var sp = source.Pawn;
		var dp = dest.Pawn;

		if ( sp == null || dp == null ) return false;
		if ( sp.Position.Distance( dp.Position ) > 2200 ) return false;

		return true;
	}

	/// <summary>
	/// Sends a log to the kill feed
	/// </summary>
	[ClientRpc]
	public override void OnKilledMessage(long leftid, string left, long rightid, string right, string method)
	{
		var _showKill = false;
		foreach(IClient cl in Game.Clients)
		{
			if(cl.SteamId == rightid && cl.Pawn is Player ply)
			{
				_showKill = ply.InGame;
				break;
			}
		}
		if(_showKill) KillFeed.Current?.AddEntry(leftid, left, rightid, right, method);
	}
}

public class LossInformation : BaseNetworkable
{
	public IClient client = null;
	public float timeAlive = 0f;
	public int eventCount = 0;
	public int position = 1;

	public LossInformation(IClient _client, float _time, int _events, int _position)
	{
		client = _client;
		timeAlive = _time;
		eventCount = _events;
		position = _position;
	}
}