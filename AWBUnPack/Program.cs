using System.IO;
using System.Reflection;
using System.Reflection.PortableExecutable;

namespace AWBUnPack
{
    internal class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0 || args[0].Equals("-h", StringComparison.CurrentCultureIgnoreCase) || args[0].Equals("-help", StringComparison.CurrentCultureIgnoreCase))
            {
                Console.WriteLine("Usage: AWBUnPack \nTo unpack an AWB: AWBUnPack <path to .AWB file>\nTo Repack an AWB: AWBUnPack <path to folder with audio files> -swi (optional argument, writes wave IDs to header as shorts)\nPress any key to exit...");
                Console.ReadKey();
                return;
            }

            bool isShortWaveID = false;

            if (args.Length > 1)
            {
                if (args[1] == "-swi") isShortWaveID = true;
            }

            bool isExtractMode = true;

            string filePath = args[0];
            if (!filePath.EndsWith(".awb", StringComparison.CurrentCultureIgnoreCase))
            {
                if (!Directory.Exists(args[0]))
                {
                    Console.WriteLine("AWBUnPack: Input File is not an AWB file!\nPress any key to exit...");
                    Console.ReadKey();
                    return;
                }

                isExtractMode = false;
            }

            uint PaddingValue = 0x20;

            if (isExtractMode)
            {
                try
                {
                    using (BinaryReader reader = new BinaryReader(File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read)))
                    {
                        reader.BaseStream.Position = 6;

                        // var ASF2Magic = reader.ReadInt32();
                        // var Field04 = reader.ReadInt32();

                        var WaveFieldLength = reader.ReadUInt16();

                        int numOfEntries = reader.ReadInt32();
                        uint targetPadding = reader.ReadUInt32(); // usually 0x20

                        List<uint> PtrList = new List<uint>();
                        List<int> WaveID = new List<int>();

                        for (int i = 0; i < numOfEntries; i++)
                        {
                            if (WaveFieldLength == 2) WaveID.Add(reader.ReadUInt16());
                            else WaveID.Add(reader.ReadInt32());
                        }

                        for (int i = 0; i < numOfEntries; i++)
                        {
                            PtrList.Add(GetRealAddress(reader.ReadUInt32(), targetPadding));
                        }

                        PtrList.Add(reader.ReadUInt32()); // filesize

                        string BaseDirectory = Path.Join(Path.GetDirectoryName(filePath), Path.GetFileNameWithoutExtension(filePath));
                        Directory.CreateDirectory(BaseDirectory);

                        for (int i = 0; i < PtrList.Count - 1; i++)
                        {
                            int fileSize = (int)(PtrList[i + 1] - PtrList[i]);
                            reader.BaseStream.Position = PtrList[i];
                            int Magic = reader.ReadInt32();
                            reader.BaseStream.Position -= 4;

                            byte[] remainingData = reader.ReadBytes(fileSize);
                            string targetPath = Path.Join(BaseDirectory, $"{WaveID[i]:D5}_streaming" + GetExtensionFromMagic(Magic));

                            using (BinaryWriter writer = new BinaryWriter(File.Open(targetPath, FileMode.Create, FileAccess.Write, FileShare.Read)))
                            {
                                Console.WriteLine($"AWBExtractRaw: Writing file {targetPath}");
                                writer.BaseStream.Write(remainingData, 0, remainingData.Length);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("An error occurred: " + e.Message);
                }
            }
            else
            {
                try
                {
                    // List to hold the extracted IDs
                    List<int> WaveIDs = new List<int>();
                    List<string> FilesToAdd = new List<string>();

                    // Get all files in the directory
                    string[] files = Directory.GetFiles(args[0]);

                    foreach (var soundfile in files)
                    {
                        string fileName = Path.GetFileName(soundfile);

                        if (fileName.Contains("_"))
                        {
                            string[] parts = fileName.Split('_');

                            if (parts.Length > 1)
                            {
                                if (int.TryParse(parts[0], out int id))
                                {
                                    WaveIDs.Add(id);
                                    FilesToAdd.Add(soundfile);
                                }
                            }
                        }
                    }

                    string targetPath = Path.Join(Path.GetDirectoryName(args[0]), new DirectoryInfo(args[0]).Name + ".awb");

                    using (BinaryWriter writer = new BinaryWriter(File.Open(targetPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Write)))
                    {
                        Console.WriteLine($"AWBUnPack: Packing AWB to {targetPath}");

                        if (isShortWaveID) Console.WriteLine("AWBUnPack: Using short Wave IDs");
                        else Console.WriteLine("AWBUnPack: Using int Wave IDs");

                        writer.Write(0x32534641); // ASF2
                        writer.Write((byte)2); // type
                        writer.Write((byte)4); // pointer field length

                        if (isShortWaveID) writer.Write((byte)2); // wave id field length
                        else writer.Write((byte)4);

                        writer.Write((byte)0); // padding

                        writer.Write(WaveIDs.Count);
                        writer.Write(PaddingValue);

                        foreach (int waveid in WaveIDs)
                        {
                            if (isShortWaveID) writer.Write((UInt16)waveid);
                            else writer.Write(waveid);
                        }

                        var PointerTableAddr = writer.BaseStream.Position;

                        foreach (int waveid in WaveIDs)
                        {
                            writer.Write(0); // dummy ptr
                        }

                        writer.Write(0x39693969); // dummy filesize

                        var paddingBytes = CalculatePadding(writer.BaseStream.Position, 0x10);
                        if (paddingBytes > 0)
                        {
                            byte[] padding = new byte[paddingBytes];
                            writer.Write(padding);
                        }

                        var HeaderEndAddr = writer.BaseStream.Position;

                        List<long> FileLocation = new List<long>();

                        foreach (string file in FilesToAdd)
                        {
                            FileLocation.Add(writer.BaseStream.Position);

                            byte[] fileBytes = File.ReadAllBytes(file);
                            writer.Write(fileBytes);

                            paddingBytes = CalculatePadding(writer.BaseStream.Position, PaddingValue);
                            if (paddingBytes > 0)
                            {
                                byte[] padding = new byte[paddingBytes];
                                writer.Write(padding);
                            }
                        }

                        paddingBytes = CalculatePadding(writer.BaseStream.Position, 0x10);
                        if (paddingBytes > 0)
                        {
                            byte[] padding = new byte[paddingBytes];
                            writer.Write(padding);
                        }

                        int totalFileSize = (int)writer.BaseStream.Length;

                        writer.BaseStream.Position = PointerTableAddr;

                        foreach (long fileLocation in FileLocation)
                        {
                            writer.Write((uint)fileLocation);
                        }

                        writer.Write(totalFileSize);

                        Console.WriteLine($"AWBUnPack: AWB written successfully");

                        Console.WriteLine($"\nAWBUnPack: Writing ASF2 header to file");

                        Stream stream = writer.BaseStream;
                        stream.Seek(0, SeekOrigin.Begin);
                        byte[] ASF2Header = new byte[HeaderEndAddr];
                        stream.Read(ASF2Header, 0, ASF2Header.Length);

                        string ASF2HeaderPath = Path.Join(Path.GetDirectoryName(args[0]), new DirectoryInfo(args[0]).Name + ".ASF2");

                        using (BinaryWriter asf2_writer = new BinaryWriter(File.Open(ASF2HeaderPath, FileMode.Create, FileAccess.Write, FileShare.Write)))
                        {
                            Console.WriteLine($"AWBUnPack: Writing file {targetPath}");
                            asf2_writer.BaseStream.Write(ASF2Header, 0, ASF2Header.Length);
                        }


                        Console.WriteLine($"AWBUnPack: ASF2 header written successfully\n");
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("An error occurred: " + e.Message);
                }
            }

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        static long CalculatePadding(long position, uint targetPadding)
        {
            return (targetPadding - position % targetPadding) % targetPadding;
        }

        static uint GetRealAddress(uint ptr, uint targetPadding)
        {
            return ptr + (targetPadding - (ptr % targetPadding)) % targetPadding;
        }

        static string GetExtensionFromMagic(int Magic)
        {
            if (Magic == 0x00414348)
            {
                return ".hca";
            }
            else if (Magic == 0x46464952)
            {
                return ".at9";
            }
            else return ".adx";
        }
    }
}
