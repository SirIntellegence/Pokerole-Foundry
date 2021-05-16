﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using Microsoft.VisualBasic.FileIO;
using Pokerole.Core;

namespace Pokerole.Tools
{
	class InitialDataImporter
	{
		//private const String csvFetchUrl = "https://raw.githubusercontent.com/XShadeSlayerXx/PokeRole-Discord.py-Base/master/";
		private const String csvFetchUrl = "https://raw.githubusercontent.com/SirIntellegence/PokeRole-Discord.py-Base/typofix3/";
		private const string MOVE_MISSING_DESCRIP = "This move is missing";
		private readonly HashSet<Guid> unevolvedEntries = new HashSet<Guid>();
		private readonly HashSet<Guid> hasMegaEvolution = new HashSet<Guid>();
		private PokeroleXmlData data;
		private XmlSerializer xmlSerializer = new XmlSerializer(typeof(PokeroleXmlData));
		private readonly Dictionary<String, Move.Builder> movesByName = new Dictionary<string, Move.Builder>();
		private readonly Dictionary<String, Item.Builder> itemsByName = new Dictionary<string, Item.Builder>();
		private readonly Dictionary<String, DexEntry.Builder> monByName = new Dictionary<string, DexEntry.Builder>();
		private readonly Dictionary<String, Ability.Builder> abilitiesByName = new Dictionary<string, Ability.Builder>();
		public InitialDataImporter()
		{
			String previousOutput;
			if (File.Exists("output.xml"))
			{
				previousOutput = File.ReadAllText("output.xml");
				data = (PokeroleXmlData)xmlSerializer.Deserialize(new StringReader(previousOutput));
				//populate dicts
				movesByName = data.Moves.ToDictionary(move => move.Name!);
				itemsByName = data.Items.ToDictionary(item => item.Name!);
				monByName = data.DexEntries.ToDictionary(entry => entry.Name!);
				abilitiesByName = data.Abilities.ToDictionary(ability => ability.Name!);
			}
			if (data == null)
			{
				data = new PokeroleXmlData();
			}
		}
		public void DoImport()
		{

			//read them datas!!!
			AddEntries(movesByName, ReadMoves());
			AddEntries(itemsByName, ReadItems());
			AddEntries(monByName, ReadDexEntries());
			AddEntries(abilitiesByName, ReadAbilities());
			ReadMoveLists();
			LinkAbilities();




			StringWriter writer = new StringWriter();
			xmlSerializer.Serialize(writer, data);
			File.WriteAllText("output.xml", writer.ToString());
			Console.WriteLine(writer.ToString());

			//for the sake of testing
			PokeroleXmlData data2 = (PokeroleXmlData)xmlSerializer.Deserialize(new StringReader(writer.ToString()));

			//Console.WriteLine("Hello World!");
		}
		private void AddEntries<K, V>(Dictionary<K, V> destination, Dictionary<K, V> toAdd) where
			K : notnull
		{
			if (destination == toAdd)
			{
				return;
			}
			foreach (var item in toAdd)
			{
				destination.Add(item.Key, item.Value);
			}
		}

		private String FetchFileIfNeeded(String file)
		{
			String path = Path.Combine(Directory.GetCurrentDirectory(), file);
			if (!File.Exists(path))
			{
				//download it!
				using var client = new WebClient();
				client.DownloadFile(csvFetchUrl + file, path);
			}
			return path;
		}
		private T ParseEnum<T>(string val) where T : Enum
		{
			return (T)Enum.Parse(typeof(T), val.Replace(" ", ""), true);
		}
		private ItemReference<ITypeDefinition>? ReadType(string val)
		{
			if (String.IsNullOrEmpty(val))
			{
				return null;
			}
			BuiltInType type;
			if (val == "any")
			{
				//"Any Move" support
				type = BuiltInType.Normal;
			}
			else
			{
				type = ParseEnum<BuiltInType>(val);
			}
			ITypeDefinition def = TypeManager.GetBuiltInType(type);
			return new ItemReference<ITypeDefinition>(def.DataId, def.Name);

		}
		private int? ReadInt(String val)
		{
			if (String.IsNullOrEmpty(val))
			{
				return null;
			}
			return int.Parse(val);
		}
		private AbilityEntry? ReadAbility(String val, bool hidden)
		{
			if (String.IsNullOrEmpty(val))
			{
				return null;
			}
			ItemReference<Ability> reference = new ItemReference<Ability>(default, val);
			return new AbilityEntry(hidden, reference);
		}
		private Dictionary<String, Move.Builder> ReadMoves()
		{
			Dictionary<String, Move.Builder> moves = new Dictionary<string, Move.Builder>();
			String file = FetchFileIfNeeded("pokeMoveSorted.csv");
			foreach (var line in File.ReadAllLines(file))
			{
				Move.Builder? builder;
				string[] fields = line.Split(new char[] { ',' }, 10);
				if (fields[0] == "Struggle")
				{
					//read a physical and special variant
					String[] physical, special;
					physical = new String[fields.Length];
					special = new String[fields.Length];
					for (int i = 0; i < fields.Length; i++)
					{
						String item = fields[i];
						if (item.Contains("/"))
						{
							String[] halves = item.Split('/');
							physical[i] = halves[0];
							special[i] = halves[1];
						}
						else
						{
							physical[i] = item;
							special[i] = item;
						}
					}
					physical[0] = "Struggle (Physical)";
					special[0] = "Struggle (Special)";
					builder = ReadMove(physical);
					if (builder != null)
					{
						data.Moves.Add(builder);
						moves.Add(builder.Name!, builder);
					}
					builder = ReadMove(special);
					if (builder != null)
					{
						data.Moves.Add(builder);
						moves.Add(builder.Name!, builder);
					}
					continue;
				}
				builder = ReadMove(fields);
				if (builder == null)
				{
					continue;
				}
				data.Moves.Add(builder);
				String? name = builder.Name;
				if (name == null)
				{
					throw new InvalidOperationException("Move name missing");
				}
				moves.Add(name, builder);
			}
			//these moves should exist, but don't, so create dummy entries for them
			String[] missingMoves =
			{
				"Poltergeist",
				"Behemoth Blade",
				"Behemoth Bash"
			};
			ISkill noneSkill = SkillManager.GetBuiltInSkill(BuiltInSkill.None);
			ITypeDefinition normalType = TypeManager.GetBuiltInType(BuiltInType.Normal);
			foreach (var moveName in missingMoves)
			{
				if (moves.ContainsKey(moveName))
				{
					continue;
				}

				Move.Builder builder = new Move.Builder()
				{
					DataId = new DataId(null, Guid.NewGuid()),
					Name = moveName,
					Type = normalType.ItemReference,
					Description = MOVE_MISSING_DESCRIP,
					MoveCategory = MoveCategory.Invalid,
					PrimaryAccuracySkill = noneSkill.ItemReference,
					SecondaryAccuracySkill = noneSkill.ItemReference,

				};
				data.Moves.Add(builder);
				moves.Add(moveName, builder);
			}
			return moves;
		}

		private Move.Builder? ReadMove(string[] fields)
		{
			Guid guid = Guid.Empty;
			//someone mispelled something...
			String moveName = fields[0].Replace("Behemot ", "Behemoth ");
			if (movesByName.TryGetValue(moveName, out Move.Builder? prevEntry))
			{
				if (prevEntry.Description != MOVE_MISSING_DESCRIP)
				{
					//we already have that one
					return null;
				}
				//use existing id to avoid issues
				guid = prevEntry.DataId!.Value.Uuid;
			}
			Move.Builder builder = new Move.Builder();
			if (guid == Guid.Empty)
			{
				guid = Guid.NewGuid();
			}
			builder.DataId = new DataId(null, guid);
			builder.Name = moveName;
			String item = fields[1];
			//BuiltInType type;
			//if (item == "any")
			//{
			//	//"Any Move" support...
			//	type = BuiltInType.Normal;
			//}
			//else
			//{
			//	type = (BuiltInType)Enum.Parse(typeof(BuiltInType), item, true);
			//}
			//ITypeDefinition typeDef = TypeManager.GetBuiltInType(type);
			builder.Type = ReadType(item);// new ItemReference<ITypeDefinition>(typeDef.DataId, typeDef.Name);

			item = fields[2];
			MoveCategory category;
			if (item == "???")
			{
				//"Any Move" support
				category = MoveCategory.Physical | MoveCategory.Special | MoveCategory.Support;
			}
			else
			{
				category = ParseEnum<MoveCategory>(item);
			}
			builder.MoveCategory = category;
			builder.Power = int.Parse(fields[3]);

			//damage skill. Can be empty
			item = fields[4];
			BuiltInSkill skill;
			ISkill skillDef;
			if (!String.IsNullOrEmpty(item))
			{
				skill = ParseEnum<BuiltInSkill>(item);
				skillDef = SkillManager.GetBuiltInSkill(skill);
				builder.DamageSkill = new ItemReference<ISkill>(skillDef.DataId, skillDef.Name);
			}
			//currently cannot handle field 6.
			//TODO: Implement handling field 6... Whatever that is....

			//Accuracy
			item = fields[6];
			bool negative = item.Contains("missing", StringComparison.OrdinalIgnoreCase);
			if (negative)
			{
				item = item.Replace("missing", "", StringComparison.OrdinalIgnoreCase);
			}
			skill = String.IsNullOrEmpty(item) ? BuiltInSkill.None : ParseEnum<BuiltInSkill>(item);
			skillDef = SkillManager.GetBuiltInSkill(skill);
			builder.PrimaryAccuracySkill = new ItemReference<ISkill>(skillDef.DataId, skillDef.Name);
			builder.PrimaryAccuracyIsNegative = negative;

			item = fields[7];
			skill = String.IsNullOrEmpty(item) ? BuiltInSkill.None : ParseEnum<BuiltInSkill>(item);
			skillDef = SkillManager.GetBuiltInSkill(skill);
			builder.SecondaryAccuracySkill = new ItemReference<ISkill>(skillDef.DataId, skillDef.Name);

			//target
			item = fields[8];
			MoveTarget target;
			if (item == "Any")
			{
				//"Any Move" support
				target = MoveTarget.Battlefield;
			}
			else
			{
				target = ParseEnum<MoveTarget>(item);
			}
			builder.MoveTarget = target;

			item = fields[9];
			if (!String.IsNullOrEmpty(item) && "-" != item)
			{
				builder.Effects.Add(item);
			}

			return builder;
		}

		private Dictionary<String, Item.Builder> ReadItems()
		{
			String file = FetchFileIfNeeded("PokeRoleItems.csv");
			Dictionary<String, Item.Builder> items = new Dictionary<string, Item.Builder>(200);
			using (TextFieldParser csvParser = new TextFieldParser(file))
			{
				csvParser.SetDelimiters(new string[] { "," });
				csvParser.HasFieldsEnclosedInQuotes = true;
				//name+descrip only for now
				while (!csvParser.EndOfData)
				{
					String[] fields = csvParser.ReadFields();
					if (String.IsNullOrEmpty(fields[1]))
					{
						//delimiter. Ignore
						continue;
					}
					String name = fields[0];
					if (itemsByName.ContainsKey(name))
					{
						//already imported
						continue;
					}
					Item.Builder builder = new Item.Builder
					{
						Name = fields[0],
						Description = fields[1]
					};
					items.Add(builder.Name, builder);
					data.Items.Add(builder);
				}
			}
			return items;
		}
		private Dictionary<String, DexEntry.Builder> ReadDexEntries()
		{
			String file = FetchFileIfNeeded("PokeroleStats.csv");
			bool first = true;
			Regex dexRegex = new Regex("^#?(D)?([0-9]+)(.*)$");
			foreach (var line in File.ReadAllLines(file))
			{
				if (first)
				{
					//skip over the header
					first = false;
					continue;
				}
				String[] items = line.Split(',');
				if (monByName.ContainsKey(items[1]))
				{
					//already imported
					continue;
				}
				DexEntry.Builder builder = new DexEntry.Builder();
				builder.DataId = new DataId(null, Guid.NewGuid());

				String rawDex = items[0];
				Match m = dexRegex.Match(rawDex);
				if (!m.Success)
				{
					//wat???
					throw new InvalidOperationException("Invalid dex num!");
				}
				int dexNum = int.Parse(m.Groups[2].Value);
				String regonalVariant = m.Groups[3].Value;
				String variant = (regonalVariant) switch
				{
					"A" => "Alolan",
					"G" => "Galarian",
					"B" => "BBF",
					"" => "",
					_ => ""//separate case for breakpoints
				};
				if (m.Groups[1].Success)
				{
					variant = "Delta";
				}
				builder.Variant = variant;
				builder.DexNum = dexNum;

				builder.Name = items[1];

				builder.PrimaryType = ReadType(items[2]);
				builder.SecondaryType = ReadType(items[3]);

				builder.BaseHp = ReadInt(items[4]);
				builder.StartingStrength = ReadInt(items[5]);
				builder.MaxStrength = ReadInt(items[6]);
				builder.StartingDexterity = ReadInt(items[7]);
				builder.MaxDexterity = ReadInt(items[8]);
				builder.StartingVitality = ReadInt(items[9]);
				builder.MaxVitality = ReadInt(items[10]);
				builder.StartingSpecial = ReadInt(items[11]);
				builder.MaxSpecial = ReadInt(items[12]);
				builder.StartingInsight = ReadInt(items[13]);
				builder.MaxInsight = ReadInt(items[14]);

				for (int i = 0; i < 4; i++)
				{
					//0&1 = primary+secondary ability
					//2 = hidden ability
					//3 = event ability. Treating as a hidden ability
					var entry = ReadAbility(items[15 + i], i > 1);
					if (entry != null)
					{
						builder.Abilities.Add(entry);
					}
				}
				//next index is 19
				if (!String.IsNullOrEmpty(items[19]))
				{
					unevolvedEntries.Add(builder.DataId.Value.Uuid);
				}
				if (!String.IsNullOrEmpty(items[20]))
				{
					hasMegaEvolution.Add(builder.DataId.Value.Uuid);
				}
				builder.SuggestedRank = ParseEnum<Rank>(items[21]);

				String genderKind = items[22];
				builder.GenderType = (genderKind.ToLowerInvariant()) switch
				{
					"f" => GenderType.FemaleOnly,
					"m" => GenderType.MaleOnly,
					"n" => GenderType.None,
					_ => GenderType.Default,
				};
				data.DexEntries.Add(builder);
				String name = builder.Name;
				//if (!String.IsNullOrEmpty(builder.Variant))
				//{
				//	name = variant + " " + name;
				//}
				monByName.Add(name, builder);
			}
			return monByName;
		}

		private Dictionary<String, Ability.Builder> ReadAbilities()
		{
			string file = FetchFileIfNeeded("PokeRoleAbilities.csv");
			using (TextFieldParser csvParser = new TextFieldParser(file))
			{
				csvParser.SetDelimiters(new string[] { "," });
				csvParser.HasFieldsEnclosedInQuotes = true;
				bool first = true;
				while (!csvParser.EndOfData)
				{
					String[] fields = csvParser.ReadFields();
					if (first)
					{
						//skip headers
						first = false;
						continue;
					}
					if (abilitiesByName.ContainsKey(fields[0]))
					{
						//we already have that one
						continue;
					}
					Ability.Builder builder = new Ability.Builder
					{
						DataId = new DataId(null, Guid.NewGuid()),
						Name = fields[0],
						Effect = fields[1]
					};
					data.Abilities.Add(builder);
					abilitiesByName.Add(builder.Name, builder);
				}
			}
			return abilitiesByName;
		}

		private void ReadMoveLists()
		{
			String file = FetchFileIfNeeded("PokeLearnMovesFull.csv");
			foreach (var line in File.ReadLines(file))
			{
				String[] fields = line.Split(",");
				String[] id = fields[0].Split(new char[] { ' ' }, 2);
				if (!monByName.TryGetValue(id[1], out DexEntry.Builder? builder))
				{
					throw new InvalidOperationException($"Could not find moves for {id[1]}");
				}
				if (builder.MoveSet.Count > 0)
				{
					//already populated
					continue;
				}

				for (int i = 0; i < fields.Length - 1; i += 2)
				{
					String moveName = fields[i + 1];
					String rawRank = fields[i + 2];
					Rank rank = ParseEnum<Rank>(rawRank);
					if (!movesByName.TryGetValue(moveName, out Move.Builder? move))
					{
						throw new InvalidOperationException($"Could not find a move called \"{moveName}\"");
					}

					MoveEntry entry = new MoveEntry(rank, move.ItemReference!.Value);
					builder.MoveSet.Add(entry);
				}
			}
		}
		private void LinkAbilities()
		{
			List<String> missingAbilities = new List<string>(10);
			foreach (var entry in monByName.Values)
			{
				List<AbilityEntry> newEntries = new List<AbilityEntry>(entry.Abilities.Count);
				foreach (var ability in entry.Abilities)
				{
					//lookup the ability
					if (!abilitiesByName.TryGetValue(ability.Ability.DisplayName!, out Ability.Builder? abilityBuilder))
					{
						throw new InvalidOperationException($"Unknown ability: {ability.Ability.DisplayName}");
						//missingAbilities.Add(ability.Ability.DisplayName!);
						//newEntries.Add(ability);
						//continue;
					}
					newEntries.Add(new AbilityEntry(ability.Hidden, abilityBuilder.ItemReference!.Value));
				}
				entry.Abilities = newEntries;
			}
			if (missingAbilities.Count > 0)
			{
				Debugger.Break();
			}

		}

	}
}
