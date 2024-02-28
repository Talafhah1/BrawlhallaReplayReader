using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace BrawlhallaReplayReader
{
	///<summary>Class <c>Replay</c> reads a Brawlhalla replay file.</summary>
	///<remarks>
	///<para>This may not work with replays with a version lower than <c>220</c> (game version <c>7.05</c>).<br/>
	///Based on <a href="https://github.com/itselectroz/brawlhalla-replay-reader/">brawlhalla-replay-reader</a> by itselectroz.</para>
	///</remarks>
	public class Replay
	{
		///<value>Length of the replay in frames.</value>
		public uint Length { get; private set; }

		///<value>The ID of the match's End of Match Fanfare (EndMatchVoiceLine).</value>
		public uint EndOfMatchFanFareID { get; private set; }

		///<Value>Results of the match.</Value>
		public FrozenDictionary<int, int> Results => m_results.ToFrozenDictionary();

		///<value>Deaths that happened during the match.</value>
		public ReadOnlyCollection<DeathType> Deaths => m_deaths.AsReadOnly();

		///<value>Random seed used for random events.</value>
		public int RandomSeed { get; private set; }

		///<value>Version of the replay.</value>
		public uint Version { get; private set; }

		///<value>First version check of the replay.  Valid replays will have this value equal to <c>Version</c>.</value>
		public uint VersionCheck1 { get; private set; }

		///<value>Second version check of the replay.  Valid replays will have this value equal to <c>Version</c>.</value>
		public uint VersionCheck2 { get; private set; }

		///<value>Playlist ID of the match.</value>
		public uint PlaylistID { get; private set; }

		///<value>Playlist string key of the match.</value>
		public string? PlaylistName { get; private set; }

		///<value>If this match was played online.</value>
		public bool OnlineGame { get; private set; }

		///<value>Game settings of the match.</value>
		public GameSettingsType GameSettings { get; private set; } = default!;

		///<value>Level ID of the match.</value>
		public uint LevelID { get; private set; }

		///<value>Number of Legends per player.</value>
		public ushort HeroCount { get; private set; }

		///<value>Checksum of the replay.</value>
		public uint Checksum { get; private set; }

		///<value>Entities in the match.</value>
		public ReadOnlyCollection<EntityType> Entities => m_entities.AsReadOnly();

		///<value>Inputs that happened during the match.</value>
		public FrozenDictionary<int, ReadOnlyCollection<InputType>> Inputs => m_inputs.ToFrozenDictionary(x => x.Key, x => x.Value.AsReadOnly());

		///<value>Array used to decipher the replay.</value>
		private static readonly byte[] s_xor_key = [0x6B, 0x10, 0xDE, 0x3C, 0x44, 0x4B, 0xD1, 0x46, 0xA0, 0x10, 0x52, 0xC1, 0xB2, 0x31, 0xD3, 0x6A, 0xFB, 0xAC, 0x11, 0xDE, 0x06, 0x68, 0x08, 0x78, 0x8C, 0xD5, 0xB3, 0xF9, 0x6A, 0x40, 0xD6, 0x13, 0x0C, 0xAE, 0x9D, 0xC5, 0xD4, 0x6B, 0x54, 0x72, 0xFC, 0x57, 0x5D, 0x1A, 0x06, 0x73, 0xC2, 0x51, 0x4B, 0xB0, 0xC9, 0x8C, 0x78, 0x04, 0x11, 0x7A, 0xEF, 0x74, 0x3E, 0x46, 0x39, 0xA0, 0xC7, 0xA6];

		///<value>If the checksum and two version checks should be ignored and raise an exception if they don't match.</value>
		private readonly bool mb_ignore_checksum;

		///<value>Bit stream used to read the replay.</value>
		private readonly BitStream m_data;

		///<Value>Results of the match.</Value>
		private readonly Dictionary<int, int> m_results = [];

		///<value>Deaths that happened during the match.</value>
		private List<DeathType> m_deaths = [];

		///<value>Entities in the match.</value>
		private readonly List<EntityType> m_entities = [];

		///<value>Inputs that happened during the match.</value>
		private readonly Dictionary<int, List<InputType>> m_inputs = [];

		///<summary>Reads and deciphers a replay file.</summary>
		///<param name="path">Path to the replay file.</param>
		///<param name="ignore_checksum">If the checksum and two version checks should be ignored and raise an exception if they don't match.</param>
		///<exception cref="FileNotFoundException">Thrown when the replay file is not found.</exception>
		///<exception cref="InvalidReplayStateException">Thrown when the replay file is in an invalid state.</exception>
		///<exception cref="ReplayVersionException">Thrown when the replay's version doesn't match the version stored in the header.</exception>
		///<exception cref="ReplayChecksumException">Thrown when the calculated checksum does not match the replay's checksum stored in the header.</exception>
		///<exception cref="InvalidReplayDataException"> thrown when the replay's data is invalid.</exception>
		///<remarks>
		///<para>This may not work with replays with a version lower than <c>234</c>.<br/>
		///Based on <a href="https://github.com/itselectroz/brawlhalla-replay-reader/">brawlhalla-replay-reader</a> by itselectroz.</para>
		///</remarks>
		public Replay(Stream stream, bool ignore_checksum = false)
		{
			mb_ignore_checksum = ignore_checksum;
			byte[] decompressed_replay = Utils.DecompressStream(stream);
			for (int i = 0; i < decompressed_replay.Length; i++) decompressed_replay[i] ^= s_xor_key[i % s_xor_key.Length];
			m_data = new(decompressed_replay);
			bool end_of_replay = false;
			while (m_data.RemainingBytes > 0 && !end_of_replay)
			{
				byte replay_state = (byte)m_data.ReadBits(3);
				switch (replay_state)
				{
					case 1: { ReadInputs(); break; }
					case 2: { end_of_replay = true; break; }
					case 3: { ReadHeader(); break; }
					case 4: { ReadPlayerData(); break; }
					case 6: { ReadResults(); break; }
					case 5:
					case 7: { ReadFaces(replay_state == 5); break; }
					default: { throw new InvalidReplayStateException("Invalid replay state."); }
				}
			}
			if (!mb_ignore_checksum && CalculateChecksum() != Checksum) throw new ReplayChecksumException("Calculated checksum does not match the replay's Checksum stored in the header.");
			// The game checks if LevelID corresponds to a valid map; this is not implemented here.
			m_data = new([]);
		}

		///<summary>Reads the inputs from the replay.</summary>
		private void ReadInputs()
		{
			while (m_data!.ReadBool())
			{
				byte entity_id = (byte)m_data.ReadBits(5);
				int input_count = m_data.ReadInt();
				if (!m_inputs.ContainsKey(entity_id)) m_inputs.Add(entity_id, []);
				for (int i = 0; i < input_count; i++)
				{
					int time_stamp = m_data.ReadInt();
					ushort input_state = m_data.ReadBool() ? (ushort)m_data.ReadBits(14) : (ushort)0;
					m_inputs[entity_id].Add(new InputType(time_stamp, input_state));
				}
			}
		}

		///<summary>Reads the header from the replay.</summary>
		private void ReadHeader()
		{
			RandomSeed = m_data!.ReadInt();
			Version = (uint)m_data.ReadInt();
			PlaylistID = (uint)m_data.ReadInt();
			if (PlaylistID != 0) PlaylistName = m_data.ReadString();
			OnlineGame = m_data.ReadBool();
		}

		///<summary>Reads the player data from the replay.</summary>
		///<exception cref="InvalidReplayDataException"> Thrown when the replay's data is invalid.</exception>
		///<exception cref="ReplayVersionException">Thrown when the replay's version doesn't match the version stored in the header.</exception>
		private void ReadPlayerData()
		{
			GameSettings = new GameSettingsType(m_data!);
			LevelID = (uint)m_data!.ReadInt();
			HeroCount = (ushort)m_data.ReadShort();
			if (HeroCount == 0 || HeroCount > 5) throw new InvalidReplayDataException("Invalid HeroCount; must be between 1 and 5.");
			while (m_data.ReadBool()) m_entities.Add(new EntityType(m_data.ReadInt(), m_data.ReadString(), new PlayerType(m_data, HeroCount)));
			if (m_entities.Count == 0) throw new InvalidReplayDataException("No entities found in the replay.");
			if ((VersionCheck1 = (uint)m_data.ReadInt()) != Version && !mb_ignore_checksum) throw new ReplayVersionException("First version check does not match the replay's Version stored in the header.");
			Checksum = (uint)m_data.ReadInt();
		}

		///<summary>Reads the results from the replay.</summary>
		///<exception cref="ReplayVersionException">Thrown when the replay's version doesn't match the version stored in the header.</exception>
		private void ReadResults()
		{
			Length = (uint)m_data!.ReadInt();
			if ((VersionCheck2 = (uint)m_data.ReadInt()) != Version && !mb_ignore_checksum) throw new ReplayVersionException("Second version check does not match the replay's Version stored in the header.");
			if (m_data.ReadBool())
			{
				while (m_data.ReadBool())
				{
					byte entity_id = (byte)m_data.ReadBits(5);
					short result = m_data.ReadShort();
					m_results[entity_id] = result;
				}
			}
			EndOfMatchFanFareID = (uint)m_data.ReadInt();
		}

		///<summary>Reads the faces and deaths from the replay.</summary>
		private void ReadFaces(bool knockout)
		{
			if (knockout) m_deaths = [];
			while (m_data!.ReadBool())
			{
				byte entity_id = (byte)m_data.ReadBits(5);
				int time_stamp = m_data.ReadInt();
				if (knockout) m_deaths.Add(new DeathType(time_stamp, entity_id));
			}
			if (knockout) m_deaths.Sort((a, b) => a.TimeStamp - b.TimeStamp);
		}

		///<summary>Calculates the checksum of the replay.</summary>
		///<returns>Checksum of the replay.</returns>
		public uint CalculateChecksum()
		{
			uint checksum = 0;
			foreach (EntityType entity in m_entities)
			{
				PlayerType player = entity.Player;
				checksum += player.ColorSchemeID * 5;
				checksum += player.SpawnBotID * 93;
				checksum += player.EmitterID * 97;
				checksum += player.PlayerThemeID * 53;
				for (byte i = 0; i < 8; i++) checksum += (uint)(player.Taunts[i] * (13 + i));
				checksum += (uint)player.WinTauntID * 37;
				checksum += (uint)player.LoseTauntID * 41;
				foreach (var item in player.TauntDatabase.Select((value, index) => new { value, index }))
				{
					uint field = item.value;
					uint index = (uint)item.index;
					checksum += Utils.PopulationCount(field) * (11 + index);
				}
				checksum += (uint)player.Team * 43;
				foreach (var item in player.Heroes.Select((value, index) => new { value, index }))
				{
					ReplayHeroType hero = item.value;
					uint index = (uint)item.index;
					checksum += hero.HeroID * (17 + index);
					checksum += hero.CostumeID * (7 + index);
					checksum += hero.StanceIndex * (3 + index);
					checksum += (uint)(hero.WeaponSkin2 << 16 | hero.WeaponSkin1) * (2 + index);
				}
				if (!player.HandicapsEnabled) checksum += 29;
				else
				{
					checksum += (uint)player.HandicapStockCount! * 31;
					checksum += (uint)Math.Round((decimal)(player.HandicapDamageDoneMultiplier! / 10.0)) * 3;
					checksum += (uint)Math.Round((decimal)(player.HandicapDamageTakenMultiplier! / 10.0)) * 23;
				}
			}
			checksum += LevelID * 47;
			return checksum % 173;
		}

		///<summary>Converts the <c>Replay</c> to a string.</summary>
		///<returns>String representation of the <c>Replay</c>.</returns>
		///<remarks>
		///<para>Only the header and results are included in the string representation.</para>
		///</remarks>
		public override string ToString() => $"Replay {{ Length: {Length}, EndOfMatchFanFareID: {EndOfMatchFanFareID}, Results: [{string.Join(", ", Results)}], Deaths: [{string.Join(", ", Deaths)}], RandomSeed: {RandomSeed}, Version: {Version}, VersionCheck1: {VersionCheck1}, VersionCheck2: {VersionCheck2}, PlaylistID: {PlaylistID}, PlaylistName: {PlaylistName}, OnlineGame: {OnlineGame}, GameSettings: {GameSettings}, LevelID: {LevelID}, HeroCount: {HeroCount}, Checksum: {Checksum}, Entities: [{string.Join(", ", Entities)}], Inputs: [{string.Join(", ", Inputs)}] }}";

		///<summary>Converts the <c>Replay</c> to a JSON string.</summary>
		///<returns>JSON string representation of the <c>Replay</c>.</returns>
		public string ToJson() => JsonSerializer.Serialize(this, Utils.s_json_serializer_options).Replace("  ", "\t");
	}

	///<summary>Struct <c>PlayerType</c> stores information about a player.</summary>
	public readonly struct PlayerType
	{
		///<value>The player's Color Scheme ID.</value>
		public uint ColorSchemeID { get; private init; }

		///<value>The player's Sidekick (SpawnBot) ID.</value>
		public uint SpawnBotID { get; private init; }

		///<value>The player's KO Effect (Emitter) ID.</value>
		public uint EmitterID { get; private init; }

		///<value>The player's UI Theme (PlayerTheme) ID.</value>
		public uint PlayerThemeID { get; private init; }

		///<value>The 8 IDs of the player's Emotes (Taunts).</value>
		public uint[] Taunts { get; private init; } = new uint[8];

		///<value>The player's Win Emote (Taunt) ID.</value>
		public ushort WinTauntID { get; private init; }

		///<value>The player's Lose Emote (Taunt) ID.</value>
		public ushort LoseTauntID { get; private init; }

		///<value>A bit field of the player's Emotes (Taunts).</value>
		///<remarks>Each entry represents 32 Emotes.<remarks>
		public ReadOnlyCollection<uint> TauntDatabase => m_taunt_database.AsReadOnly();

		///<value>The player's Avatar ID.</value>
		public uint AvatarID { get; private init; }

		///<value>The player's team number.</value>
		public int Team { get; private init; }

		///<value>The time the player connected to the server.</value>
		public int ConnectionTime { get; private init; }

		///<value>The player's Legend (Hero) IDs.</value>
		public ReadOnlyCollection<ReplayHeroType> Heroes => m_heroes.AsReadOnly();

		///<value>Whether the player is a bot.</value>
		public bool IsBot { get; private init; }

		///<value>Whether handicaps are enabled.</value>
		public bool HandicapsEnabled { get; private init; }

		///<value>The player's handicap stock count.</value>
		public uint? HandicapStockCount { get; private init; }

		///<value>The player's handicap damage done multiplier in percentage points.</value>
		public uint? HandicapDamageDoneMultiplier { get; private init; }

		///<value>The player's handicap damage taken multiplier in percentage points.</value>
		public uint? HandicapDamageTakenMultiplier { get; private init; }

		///<value>The player's Legend (Hero) IDs.</value>
		private readonly List<ReplayHeroType> m_heroes = [];

		///<value>A bit field of the player's Emotes (Taunts).</value>
		private readonly List<uint> m_taunt_database = [];

		///<summary>Creates a new instance of <c>PlayerType</c>.</summary>
		internal PlayerType(BitStream data, uint hero_count)
		{
			ColorSchemeID = (uint)data.ReadInt();
			SpawnBotID = (uint)data.ReadInt();
			EmitterID = (uint)data.ReadInt();
			PlayerThemeID = (uint)data.ReadInt();
			for (byte i = 0; i < 8; i++) Taunts[i] = (uint)data.ReadInt();
			WinTauntID = (ushort)data.ReadShort();
			LoseTauntID = (ushort)data.ReadShort();
			while (data.ReadBool()) m_taunt_database.Add((uint)data.ReadInt());
			AvatarID = (uint)data.ReadShort();
			Team = data.ReadInt();
			ConnectionTime = data.ReadInt();
			for (uint i = 0; i < hero_count; i++) m_heroes.Add(new ReplayHeroType(data));
			IsBot = data.ReadBool();
			if (HandicapsEnabled = data.ReadBool())
			{
				HandicapStockCount = (uint)data.ReadInt();
				HandicapDamageDoneMultiplier = (uint)data.ReadInt();
				HandicapDamageTakenMultiplier = (uint)data.ReadInt();
			}
		}

		///<summary>Converts the <c>PlayerType</c> to a string.</summary>
		///<returns>String representation of the <c>PlayerType</c>.</returns>
		public override string ToString() => $"PlayerType {{ ColorSchemeID: {ColorSchemeID}, SpawnBotID: {SpawnBotID}, EmitterID: {EmitterID}, PlayerThemeID: {PlayerThemeID}, Taunts: [{string.Join(", ", Taunts)}], WinTauntID: {WinTauntID}, LoseTauntID: {LoseTauntID}, Unknown: [{string.Join(", ", TauntDatabase)}], AvatarID: {AvatarID}, Team: {Team}, ConnectionTime: {ConnectionTime}, Heroes: [{string.Join(", ", Heroes)}], IsBot: {IsBot}, HandicapsEnabled: {HandicapsEnabled}, HandicapStockCount: {HandicapStockCount}, HandicapDamageDoneMultiplier: {HandicapDamageDoneMultiplier}, HandicapDamageTakenMultiplier: {HandicapDamageTakenMultiplier} }}";

		///<summary>Converts the <c>PlayerType</c> to a JSON string.</summary>
		///<returns>JSON string representation of the <c>PlayerType</c>.</returns>
		public string ToJson() => JsonSerializer.Serialize(this, Utils.s_json_serializer_options).Replace("  ", "\t");
	}

	///<summary>Struct <c>ReplayHeroType</c> stores information about the Legend(s) used by a player during a match.</summary>
	public readonly struct ReplayHeroType
	{
		///<value>The player's Legend (Hero) ID.</value>
		public uint HeroID { get; private init; }

		///<value>The player's Skin (Costume) ID.</value>
		public uint CostumeID { get; private init; }

		///<value>The player's Stance (Rune) index.</value>
		public uint StanceIndex { get; private init; }

		///<value>The player's first Weapon Skin ID.</value>
		public ushort WeaponSkin1 { get; private init; }

		///<value>The player's second Weapon Skin ID.</value>
		public ushort WeaponSkin2 { get; private init; }

		///<summary>Creates a new instance of <c>ReplayHeroType</c>.</summary>
		internal ReplayHeroType(BitStream data)
		{
			HeroID = (uint)data.ReadInt();
			CostumeID = (uint)data.ReadInt();
			StanceIndex = (uint)data.ReadInt();
			WeaponSkin2 = (ushort)data.ReadShort();
			WeaponSkin1 = (ushort)data.ReadShort();
		}

		///<summary>Converts the <c>ReplayHeroType</c> to a string.</summary>
		///<returns>String representation of the <c>ReplayHeroType</c>.</returns>
		public override string ToString() => $"HeroID: {HeroID}, CostumeID: {CostumeID}, StanceIndex: {StanceIndex}, WeaponSkin1: {WeaponSkin1}, WeaponSkin2: {WeaponSkin2}";

		///<summary>Converts the <c>ReplayHeroType</c> to a JSON string.</summary>
		///<returns>JSON string representation of the <c>ReplayHeroType</c>.</returns>
		public string ToJson() => JsonSerializer.Serialize(this, Utils.s_json_serializer_options).Replace("  ", "\t");
	}

	///<summary>Struct <c>GameSettingsType</c> stores information about the game settings used during a match.</summary>
	public readonly struct GameSettingsType
	{
		///<value>The game's flags.</value>
		public GameModeFlags Flags { get; private init; }

		///<value>The maximum number of players.</value>
		public uint MaxPlayers { get; private init; }

		///<value>The match's maximum duration in seconds.</value>
		public uint Duration { get; private init; }

		///<value>The round's maximum duration in seconds.</value>
		public uint RoundDuration { get; private init; }

		///<value>The number of lives each player starts with.</value>
		public uint StartingLives { get; private init; }

		///<value>The match's Gamemode (ScoringType) ID.</value>
		public uint ScoringTypeID { get; private init; }

		///<value>The score required to win the match.</value>
		public uint ScoreToWin { get; private init; }

		///<value>The match's game speed modifier in percentage points.</value>
		public uint GameSpeed { get; private init; }

		///<value>The match's damage ratio modifier in percentage points.</value>
		public uint DamageMultiplier { get; private init; }

		///<value>The match's Level Set ID.</value>
		public uint LevelSetID { get; private init; }

		///<value>Gadgets & Weapons setting.</value>
		///<remarks>A value of <c>0</c> means all Gadgets and Weapon Crates are enabled.</remarks>
		///<remarks>A value of <c>1</c> means custom Gadgets and Weapon Crates are enabled.</remarks>
		///<remarks>A value of <c>2</c> means no Gadgets are enabled.</remarks>
		public GadgetSelectType GadgetSelection { get; private init; }

		///<value>Custom Gadgets field.</value>
		///<remarks>Only valid when <c>GadgetSelection</c> is set to <c>1</c> (Custom).</remarks>
		public CustomGadgetsType CustomGadgetsField { get; private init; }

		///<summary>Creates a new instance of <c>GameSettingsType</c>.</summary>
		internal GameSettingsType(BitStream data)
		{
			Flags = new((uint)data.ReadInt());
			MaxPlayers = (uint)data.ReadInt();
			Duration = (uint)data.ReadInt();
			RoundDuration = (uint)data.ReadInt();
			StartingLives = (uint)data.ReadInt();
			ScoringTypeID = (uint)data.ReadInt();
			ScoreToWin = (uint)data.ReadInt();
			GameSpeed = (uint)data.ReadInt();
			DamageMultiplier = (uint)data.ReadInt();
			LevelSetID = (uint)data.ReadInt();
			GadgetSelection = (GadgetSelectType)data.ReadInt();
			CustomGadgetsField = new((uint)data.ReadInt());
		}

		///<summary>Converts the <c>GameSettingsType</c> to a string.</summary>
		///<returns>String representation of the <c>GameSettingsType</c>.</returns>
		public override string ToString() => $"GameSettingsType: Flags={Flags}, MaxPlayers={MaxPlayers}, Duration={Duration}, RoundDuration={RoundDuration}, StartingLives={StartingLives}, ScoringTypeID={ScoringTypeID}, ScoreToWin={ScoreToWin}, GameSpeed={GameSpeed}, DamageMultiplier={DamageMultiplier}, LevelSetID={LevelSetID}, GadgetSelection={GadgetSelection}, CustomGadgetsField={CustomGadgetsField}";

		///<summary>Converts the <c>GameSettingsType</c> to a JSON string.</summary>
		///<returns>JSON string representation of the <c>GameSettingsType</c>.</returns>
		public string ToJson() => JsonSerializer.Serialize(this, Utils.s_json_serializer_options).Replace("  ", "\t");
	}

	///<summary>Struct <c>GameModeFlags</c> stores information about the game mode flags used during a match.</summary>
	public readonly struct GameModeFlags
	{
		///<value>Whether teams are enabled.</value>
		public bool Teams { get; private init; }

		///<value>Whether team damage is enabled.</value>
		public bool TeamDamage { get; private init; }

		///<value>Whether the camera is fixed.</value>
		public bool FixedCamera { get; private init; }

		///<value>Whether gadgets are disabled.</value>
		public bool GadgetsOff { get; private init; }

		///<value>Whether weapons are disabled.</value>
		public bool WeaponsOff { get; private init; }

		///<value>Whether test Levels are enabled.</value>
		public bool TestLevelsOn { get; private init; }

		///<value>Whether Test Features are enabled.</value>
		public bool TestFeaturesOn { get; private init; }

		///<value>Whether Ghost Rule is enabled.</value>
		public bool GhostRule { get; private init; }

		///<value>Whether seasonal map decorations are disabled.</value>
		public bool TurnOffMapArtThemes { get; private init; }

		///<value>Whether Crew Battle is enabled.</value>
		public bool ForceCrewBattleCycle { get; private init; }

		///<value>Whether Advanced Settings are enabled.</value>
		///<remarks>Advanced Settings are not implemented in the live game.</remarks>
		public bool AdvancedSettings { get; private init; }

		///<summary>Creates a new instance of <c>GameModeFlags</c>.</summary>
		internal GameModeFlags(uint flags)
		{
			Teams = (flags & 0b0000000001) != 0;
			TeamDamage = (flags & 0b0000000010) != 0;
			FixedCamera = (flags & 0b00000000100) != 0;
			GadgetsOff = (flags & 0b00000001000) != 0;
			WeaponsOff = (flags & 0b00000010000) != 0;
			TestLevelsOn = (flags & 0b00000100000) != 0;
			TestFeaturesOn = (flags & 0b00001000000) != 0;
			GhostRule = (flags & 0b00010000000) != 0;
			TurnOffMapArtThemes = (flags & 0b00100000000) != 0;
			ForceCrewBattleCycle = (flags & 0b01000000000) != 0;
			AdvancedSettings = (flags & 0b10000000000) != 0;
		}

		///<summary>Converts the <c>GameModeFlags</c> to a string.</summary>
		///<returns>String representation of the <c>GameModeFlags</c>.</returns>
		public override string ToString() => $"Teams: {Teams}, Team Damage: {TeamDamage}, Fixed Camera: {FixedCamera}, Gadgets Off: {GadgetsOff}, Weapons Off: {WeaponsOff}, Test Levels On: {TestLevelsOn}, Test Features On: {TestFeaturesOn}, Ghost Rule: {GhostRule}, Turn Off Map Art Themes: {TurnOffMapArtThemes}, Force Crew Battle Cycle: {ForceCrewBattleCycle}, Advanced Settings: {AdvancedSettings}";

		///<summary>Converts the <c>GameModeFlags</c> to a JSON string.</summary>
		///<returns>JSON string representation of the <c>GameModeFlags</c>.</returns>
		public string ToJson() => JsonSerializer.Serialize(this, Utils.s_json_serializer_options).Replace("  ", "\t");
	}

	///<summary>Enum <c>GadgetSelectType</c> represents the different values for the Gadgets & Weapons setting.</summary>
	public enum GadgetSelectType
	{
		///<value>All Gadgets and Weapon Crates.</value>
		All = 0,

		///<value>Custom.</value>
		Custom = 1,

		///<value>No Gadgets.</value>
		NoGadgets = 2
	}

	///<summary>Struct <c>CustomGadgetsType</c> stores information about custom Gadgets.</summary>
	public readonly struct CustomGadgetsType
	{
		///<value>Whether Bouncy Bombs are enabled.</value>
		public bool BouncyBombs { get; private init; }

		///<value>Whether Pressure Mines are enabled.</value>
		public bool PressureMines { get; private init; }

		///<value>Whether Spikeballs are enabled.</value>
		public bool Spikeballs { get; private init; }

		///<value>Whether Sidekick Summoners are enabled.</value>
		public bool SidekickSummoners { get; private init; }

		///<value>Whether Homing Boomerangs are enabled.</value>
		public bool HomingBoomerangs { get; private init; }

		///<value>Whether Sticky Bombs are enabled.</value>
		public bool StickyBombs { get; private init; }

		///<value>Whether Weapon Crates are enabled.</value>
		public bool WeaponCrates { get; private init; }

		///<summary>Creates a new instance of <c>CustomGadgetsType</c>.</summary>
		internal CustomGadgetsType(uint custom_gadgets)
		{
			BouncyBombs = (custom_gadgets & 0b0000001) == 0;
			PressureMines = (custom_gadgets & 0b0000010) == 0;
			Spikeballs = (custom_gadgets & 0b0000100) == 0;
			SidekickSummoners = (custom_gadgets & 0b0001000) == 0;
			HomingBoomerangs = (custom_gadgets & 0b0010000) == 0;
			StickyBombs = (custom_gadgets & 0b0100000) == 0;
			WeaponCrates = (custom_gadgets & 0b1000000) == 0;
		}

		///<summary>Converts the <c>CustomGadgetsType</c> to a string.</summary>
		///<returns>String representation of the <c>CustomGadgetsType</c>.</returns>
		public override string ToString() => $"Bouncy Bombs: {BouncyBombs}, Pressure Mines: {PressureMines}, Spikeballs: {Spikeballs}, Sidekick Summoners: {SidekickSummoners}, Homing Boomerangs: {HomingBoomerangs}, Sticky Bombs: {StickyBombs}, Weapon Crates: {WeaponCrates}";

		///<summary>Converts the <c>CustomGadgetsType</c> to a JSON string.</summary>
		///<returns>JSON string representation of the <c>CustomGadgetsType</c>.</returns>
		public string ToJson() => JsonSerializer.Serialize(this, Utils.s_json_serializer_options).Replace("  ", "\t");
	}

	///<summary>Struct <c>EntityType</c> stores information about an entity during a match.</summary>
	public readonly struct EntityType
	{
		///<value>The entity's ID.</value>
		public int EntityID { get; private init; }

		///<value>The entity's name.</value>
		public string Name { get; private init; }

		///<value>The corresponding player for this entity.</value>
		public PlayerType Player { get; private init; }

		///<summary>Creates a new instance of <c>EntityType</c>.</summary>
		internal EntityType(int id, string name, PlayerType player_type)
		{
			EntityID = id;
			Name = name;
			Player = player_type;
		}

		///<summary>Converts the <c>EntityType</c> to a string.</summary>
		///<returns>String representation of the <c>EntityType</c>.</returns>
		public override string ToString() => $"Entity {EntityID} ({Name}) is controlled by player: {Player}.";

		///<summary>Converts the <c>EntityType</c> to a JSON string.</summary>
		///<returns>JSON string representation of the <c>EntityType</c>.</returns>
		public string ToJson() => JsonSerializer.Serialize(this, Utils.s_json_serializer_options).Replace("  ", "\t");

	}

	///<summary>Struct <c>DeathType</c> stores information about a player's death.</summary>
	public readonly struct DeathType
	{
		///<value>The time of the death in frames.</value>
		public int TimeStamp { get; private init; }

		///<value>The ID of the entity that died.</value>
		public int EntityID { get; private init; }

		///<summary>Creates a new instance of <c>DeathType</c>.</summary>
		internal DeathType(int time_stamp, int entity_id)
		{
			TimeStamp = time_stamp;
			EntityID = entity_id;
		}

		///<summary>Converts the <c>DeathType</c> to a string.</summary>
		///<returns>String representation of the <c>DeathType</c>.</returns>
		public override string ToString() => $"Entity {EntityID} died at frame{TimeStamp}.";

		///<summary>Converts the <c>DeathType</c> to a JSON string.</summary>
		///<returns>JSON string representation of the <c>DeathType</c>.</returns>
		public string ToJson() => JsonSerializer.Serialize(this, Utils.s_json_serializer_options).Replace("  ", "\t");
	}

	///<summary>Struct <c>InputType</c> stores an input state and the frame it happened on.</summary>
	public readonly struct InputType
	{
		///<value>Frame the input happened on.</value>
		public int TimeStamp { get; private init; }

		///<value>Whether the player is pressing the Up or Jump + Aim Up buttons.</value>
		public bool AimUp { get; private init; }

		///<value>Whether the player is pressing the Down button.</value>
		public bool Drop { get; private init; }

		///<value>Whether the player is pressing the Left button.</value>
		public bool MoveLeft { get; private init; }

		///<value>Whether the player is pressing the Right button.</value>
		public bool MoveRight { get; private init; }

		///<value>Whether the player is pressing the Jump or Jump + Aim Up buttons.</value>
		///<remarks>Jumps always clear whatever Emote the player is performing.</remarks>
		public bool Jump { get; private init; }

		///<value>Whether a Neutral direction takes priority over a Side direction when performing an attack.</value>
		///<remarks>
		///<para>This is not to be confused with the Controls setting of the same name.<br/>
		///Pressing the Aim Up button will always set this bit to true.<br/>
		///Pressing the Jump + Aim Up button will only set this bit to true if the Controls setting is also set.</para>
		///</remarks>
		public bool PrioritiseNeutralOverSide { get; private init; }

		///<value>Whether the player is pressing the Heavy Attack button.</value>
		public bool HeavyAttack { get; private init; }

		///<value>Whether the player is pressing the Light Attack button.</value>
		public bool LightAttack { get; private init; }

		///<value>Whether the player is pressing the Dodge/Dash button.</value>
		public bool DodgeDash { get; private init; }

		///<value>Whether the player is pressing the Pick Up/Throw Item button.</value>
		public bool PickUpThrow { get; private init; }

		///<value>Which Emote (Taunt), if any, the player is performing.  A value of 0 means no Emote is being performed.</value>
		public byte Taunt { get; private init; }

		///<summary>Creates a new instance of <c>InputType</c>.</summary>
		internal InputType(int time_stamp, ushort input_state)
		{
			TimeStamp = time_stamp;
			AimUp = (input_state & 0b0000_0000_00_0001) != 0;
			Drop = (input_state & 0b0000_0000_00_0010) != 0;
			MoveLeft = (input_state & 0b0000_0000_00_0100) != 0;
			MoveRight = (input_state & 0b0000_0000_00_1000) != 0;
			Jump = (input_state & 0b0000_0000_01_0000) != 0;
			PrioritiseNeutralOverSide = (input_state & 0b0000_0000_10_0000) != 0;
			HeavyAttack = (input_state & 0b0000_0001_00_0000) != 0;
			LightAttack = (input_state & 0b0000_0010_00_0000) != 0;
			DodgeDash = (input_state & 0b0000_0100_00_0000) != 0;
			PickUpThrow = (input_state & 0b0000_1000_00_0000) != 0;
			switch (input_state & 0b1111_0000_00_0000)
			{	
				case 0b0001_0000000000: { Taunt = 1; break; }
				case 0b0011_0000000000: { Taunt = 2; break; }
				case 0b0010_0000000000: { Taunt = 3; break; }
				case 0b0110_0000000000: { Taunt = 4; break; }
				case 0b0100_0000000000: { Taunt = 5; break; }
				case 0b1100_0000000000: { Taunt = 6; break; }
				case 0b1000_0000000000: { Taunt = 7; break; }
				case 0b1001_0000000000: { Taunt = 8; break; }
				default: { Taunt = 0; break; }
			}
		}

		///<summary>Converts the <c>InputType</c> to a string.</summary>
		///<returns>String representation of the <c>InputType</c>.</returns>
		public override string ToString() => $"TimeStamp: {TimeStamp}, AimUp: {AimUp}, Drop: {Drop}, MoveLeft: {MoveLeft}, MoveRight: {MoveRight}, Jump: {Jump}, PrioritiseNeutralOverSide: {PrioritiseNeutralOverSide}, HeavyAttack: {HeavyAttack}, LightAttack: {LightAttack}, DodgeDash: {DodgeDash}, PickUpThrow: {PickUpThrow}, Taunt: {Taunt}";

		///<summary>Converts the <c>InputType</c> to a JSON string.</summary>
		///<returns>JSON string representation of the <c>InputType</c>.</returns>
		public string ToJson() => JsonSerializer.Serialize(this, Utils.s_json_serializer_options).Replace("  ", "\t");
	}
}
