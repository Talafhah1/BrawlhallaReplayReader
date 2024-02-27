namespace BrawlhallaReplayReader
{
	class Program
	{
		///<summary>Entry point of the program.</summary>
		///<param name="args">The command line arguments.</param>
		public static void Main(string[] args)
		{
			string input;
			string output = "";
			bool ignore_checks = false;

			if (args.Length == 0 || args[0] == "-h" || args[0] == "--help" || args[0] == "-?" || args[0] == "/h" || args[0] == "/?") { PrintHelp(); return; }
			else if (args.Length > 3) { Console.WriteLine("[041m[97m  ERROR  [00m[91m {0}[00m", "Too many arguments."); return; }
			else
			{
				input = args[0];
				if (!File.Exists(input)) input = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "BrawlhallaReplays", input);
				if (!input.EndsWith(".replay")) input += ".replay";
				if (args.Length > 1)
				{
					if (args[1] == "-i" || args[1] == "--ignore-checks" || args[1] == "/i") ignore_checks = true;
					else output = args[1];
				}
				if (args.Length > 2) if (args[2] == "-i" || args[2] == "--ignore-checks" || args[2] == "/i") ignore_checks = true;
			}
			if (output == "") output = Path.Combine(Directory.GetCurrentDirectory(), Path.GetFileNameWithoutExtension(input) + ".json");

			Replay replay;
			try
			{
				replay = new(File.Open(input, FileMode.Open, FileAccess.Read), ignore_checks);
				File.WriteAllText(output, replay.ToJson());
			}
			catch (Exception e) { Console.WriteLine("[041m[97m  ERROR  [00m[91m {0}[00m", e.Message); return; }
			Console.WriteLine("[042m[97m SUCCESS [00m[92m Extraction finished.[00m");
			return;
		}

		///<summary>Prints the help message.</summary>
		private static void PrintHelp()
		{
			Console.WriteLine("Usage: BrawlhallaReplayReader [input.replay] [output.json] [-i]");
			Console.WriteLine("\tinput.replay: The replay file to read.  A name or path can be specified.  If not found, the program will look in the BrawlhallaReplays directory.");
			Console.WriteLine("\toutput.json: The file to write the replay data to.  If not specified, the output file will have the same name as the input file, but with a .json extension.");
			Console.WriteLine("\t-i: Ignore checks and read the replay data as is.");
			Console.WriteLine("\t-h: Print this help message.");
			return;
		}
	}
}