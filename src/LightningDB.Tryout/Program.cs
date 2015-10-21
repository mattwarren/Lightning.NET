using LightningDB.Native;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace LightningDB.Tryout
{
    class Program
    {
        static void Main(string[] args)
        {
            for (int i = 0; i < 3; i++)
                DoTest();
            for (int i = 0; i < 3; i++)
                DoTest(useRandomKeys: true);
        }

        private static void DoTest(bool useRandomKeys = false)
        {
            var env = new LightningEnvironment(CreateNewDirectoryForTest());
            env.MaxDatabases = 2;
            env.MapSize = 1000 * 1024 * 1025; // 100 MB
            env.Open();

            try
            {
                DoTestImpl(env, useRandomKeys);
            }
            finally
            {
                if (env != null)
                    env.Dispose();
            }
        }

        private static void DoTestImpl(LightningEnvironment env, bool useRandomKeys = false)
        {
            var numItemsToWrite = 1 * 1000; // One thousand
            //var numItemsToWrite = 10 * 1000; // Ten thousand
            //var numItemsToWrite = 100 * 1000; // One hundred thousand
            //var numItemsToWrite = 1 * 1000 * 1000; // 1 million
            //var numItemsToWrite = 10 * 1000 * 1000; // 10 million
            var randon = new Random(1773);
            Console.WriteLine("Using {0} keys", useRandomKeys ? "RANDOM" : "SEQUENTIAL");

            var writeTimer = Stopwatch.StartNew();
            // Need to specify DatabaseOpenFlags.IntegerKey if we want the items to be sorted, not entirely sure why though,
            // probably big/little endian related, see http://www.openldap.org/lists/openldap-bugs/201308/msg00050.html
            var dbConfig = new DatabaseConfiguration { Flags = DatabaseOpenFlags.Create | DatabaseOpenFlags.IntegerKey };
            using (var tx = env.BeginTransaction())
            using (var db = tx.OpenDatabase("test", dbConfig))
            {
                for (var i = 0; i < numItemsToWrite; ++i)
                {
                    var key = i;
                    if (useRandomKeys)
                        key = randon.Next();

                    //tx.Put(db, BitConverter.GetBytes(i), BitConverter.GetBytes(i));
                    var text = "Some Text plus: 'Key' = " + key.ToString("N0"); // + " " + new String('c', 1000);
                    var data = GetBinaryEncodedString(text);
                    tx.Put(db, BitConverter.GetBytes(key), data);
                }
                tx.Commit();
                var stats = tx.GetStats(db);
                Console.WriteLine("Stats\n  # Entries: {0:N0}\n  Depth: {1}\n  Page Size: {2}",
                                  stats.ms_entries.ToInt64(), stats.ms_depth, stats.ms_psize);
                Console.WriteLine("  Branch Pages: {0:N0}\n  Leaf Pages: {1}\n  Overflow Pages: {2}",
                                  stats.ms_branch_pages.ToInt64(), stats.ms_leaf_pages.ToInt64(), stats.ms_overflow_pages.ToInt64());
            }
            writeTimer.Stop();
            Console.WriteLine("Took {0,10:N2} ms ({1}) to WRITE {2,9:N0} values ({3,10:N0} WRITES/sec)",
                              writeTimer.Elapsed.TotalMilliseconds, writeTimer.Elapsed, numItemsToWrite,
                              numItemsToWrite / writeTimer.Elapsed.TotalMilliseconds * 1000.0);

            var readTimer = Stopwatch.StartNew();
            var readCounter = 0;
            using (var tx = env.BeginTransaction())
            using (var db = tx.OpenDatabase("test"))
            {
                tx.Get(db, BitConverter.GetBytes(int.MinValue));
                ValueStructure currentKey = default(ValueStructure);
                ValueStructure currentValue = default(ValueStructure);
                using (var cursor = tx.CreateCursor(db))
                {
                    while (cursor.MoveNext())
                    {
                        //var current = cursor.Current;
                        cursor.GetCurrent(out currentKey, out currentValue);
                        unsafe
                        {
                            var keyData = *((int*)(currentKey.data.ToPointer()));
                            int valueLengthSize;
                            var ptr = (byte*)currentValue.data.ToPointer();
                            var length = Read7BitEncodedInt(ptr, out valueLengthSize);
                            var text = new string((sbyte*)(ptr + valueLengthSize), 0, length, Encoding.UTF8);
                            //Console.WriteLine("{{ Key: {0:N0}, Value: \"{1}\" }}", keyData, text);
                        }
                        readCounter++;
                    }
                }
            }
            readTimer.Stop();
            Console.WriteLine("Took {0,10:N2} ms ({1}) to READ  {2,9:N0} values ({3,10:N0}  READS/sec)",
                              readTimer.Elapsed.TotalMilliseconds, readTimer.Elapsed, readCounter,
                              readCounter / readTimer.Elapsed.TotalMilliseconds * 1000.0);
            Console.WriteLine();
        }

        private static byte [] GetBinaryEncodedString(string text)
        {
            // find a way of not having to create the temp byte [] ("textAsBytes")
            byte[] textAsBytes = Encoding.UTF8.GetBytes(text);
            var encodedDataSize = SizeOf7BitEncodedInt(textAsBytes.Length);
            var encodedData = new byte[encodedDataSize];
            unsafe
            {
                fixed (byte* arrayPtr = encodedData)
                {
                    Write7BitEncodedInt(arrayPtr, textAsBytes.Length);
                }
            }
            var totalData = new byte[encodedDataSize + textAsBytes.Length];
            encodedData.CopyTo(totalData, 0);
            textAsBytes.CopyTo(totalData, encodedDataSize);

            return totalData;
        }

        private static byte SizeOf7BitEncodedInt(int value)
        {
            byte size = 1;
            var v = (uint)value;
            while (v >= 0x80)
            {
                size++;
                v >>= 7;
            }

            return size;
        }

        private unsafe static void Write7BitEncodedInt(byte* ptr, int value)
        {
            // Write out an int 7 bits at a time.  The high bit of the byte,
            // when on, tells reader to continue reading more bytes.
            var v = (uint)value;   // support negative numbers
            while (v >= 0x80)
            {
                *ptr = (byte)(v | 0x80);
                ptr++;
                v >>= 7;
            }
            *ptr = (byte)(v);
        }

        private unsafe static int Read7BitEncodedInt(byte* ptr, out int size)
        {
            size = 0;
            // Read out an Int32 7 bits at a time.  The high bit
            // of the byte when on means to continue reading more bytes.
            int value = 0;
            int shift = 0;
            byte b;
            do
            {
                // Check for a corrupted stream.  Read a max of 5 bytes.
                if (shift == 5 * 7)  // 5 bytes max per Int32, shift += 7
                    throw new InvalidDataException("Invalid 7bit shifted value, used more than 5 bytes");

                // ReadByte handles end of stream cases for us.
                b = *ptr;
                ptr++;
                size++;
                value |= (b & 0x7F) << shift;
                shift += 7;
            } while ((b & 0x80) != 0);

            return value;
        }

        private static string CreateNewDirectoryForTest()
        {
            var testProjectDir = Path.GetDirectoryName(typeof(Program).Assembly.Location);
            var guid = Guid.NewGuid().ToString();
            //var path = Path.Combine(Directory.GetParent(testProjectDir).Parent.FullName, "TestDb", guid);
            var path = Path.Combine(testProjectDir, "TestDb", guid);
            //Console.WriteLine("Creating folder: {0}\n{1}", guid, path);
            Console.WriteLine("Creating folder: {0}", guid);
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
