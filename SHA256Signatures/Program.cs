using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;



namespace SHA256Signatures
{
    class Program
    {
        static void Main(string[] args)
        {
            //     Encryption encr = new Encryption(args[0], Convert.ToInt32(args[1]));
            Encryption encr = new Encryption("abc.gz", 1000000000);
            encr.threadsControl();
 //           Console.ReadLine();
        }
    }

    class Encryption
    {
//        public static byte[] originalFileBytes = null; //массив байт исходного файла
        public static FileInfo originalFile; 
        public static long partSize = 0; //размер блока
        public static long lastPartSize = 0; //размер последнего блока
        public static int numParts = 0; //количество блоков
 //       public bool isLastPart = false; //флаг последнего блока
        public static string[] hash;
        public double coef = 0;
        public static long subPartSize = 0, 
                            lSubPartSize = 0;
        public static int numSubParts = 0,
                            lNumSubParts = 0;
        public static long lastSubPartSize = 0,
                            lLastSubPartSize = 0;
        public static Semaphore _pool = new Semaphore(2, 2);

        public Encryption(string _filename,long _partSize)
        {
            originalFile = new FileInfo(_filename);
            partSize = _partSize;

            //подсчет количества блоков
            long fileLength = originalFile.Length;

            //пока не достигнут конец файла
            while (fileLength > 0)
            {
                //если размер последнего блока меньше или равно заданному размеру блока
                if (fileLength <= partSize)
                {
                    lastPartSize = fileLength; //вычисление размера последнего блока
                    numParts++;
                    fileLength = 0;
                }
                else
                {
                    fileLength -= partSize;
                    numParts++;
                }
            }
            hash = new string[numParts];
            coef = lastPartSize / (double)partSize;
        }
        
        public void threadsControl()
        {
            Process currentProcess = Process.GetCurrentProcess();
            currentProcess.PriorityClass = ProcessPriorityClass.High;

            Thread[] encr = new Thread[numParts]; //создание массива потоков

            //инициализация массива потоков
            for (int i = 0; i < numParts; i++)
            {
                //каждый поток будет выполнять процедуру hashToDict с параметром
                encr[i] = new Thread(new ParameterizedThreadStart(hashToDict));
            }

            int unstartedThreads = numParts; //счетчик незапущенных потоков
            int runningThreads = 0; //счетчик выполняемых потоков
            int stoppedThreads = 0; //счетчик выполненных потоков
            
            //вычисление свободной памяти
            PerformanceCounter ramCounter = new PerformanceCounter("Memory", "Available Bytes");
            double availableMemory = ramCounter.NextValue()-2000000000;
            double occupiedMemory = 0; //счетчик занятой памяти

            long len = partSize;
            if (originalFile.Length < availableMemory-1000000000)
            {
                subPartSize = Convert.ToInt64(Math.Floor((availableMemory - 1000000000) / (numParts - 1 + coef)));
                lSubPartSize = (long)(Math.Floor(coef * subPartSize));
            }
            else
            {
                subPartSize = partSize;
                lSubPartSize = (long)(Math.Floor(coef * subPartSize));
            }
            Console.WriteLine(availableMemory/(numParts-1+coef));

            while (len > 0)
            {
                Console.WriteLine("len: {0}", len);
                //если размер последнего блока меньше или равно заданному размеру блока
                if (len <= subPartSize)
                {
                    lastSubPartSize = len; //вычисление размера последнего блока
                    numSubParts++;
                    len = 0;                    
                }
                else
                {
                    len -= subPartSize;
                    numSubParts++;
                }
            }

            len = lastPartSize;
            Console.WriteLine("size: {0}, num: {1}, last: {2}, len: {3}", subPartSize, numSubParts, lastSubPartSize,len);
            while (len > 0)
            {
                //если размер последнего блока меньше или равно заданному размеру блока
                if (len <= lSubPartSize)
                {
                    lLastSubPartSize = len; //вычисление размера последнего блока
                    lNumSubParts++;
                    len = 0;
                }
                else
                {
                    len -= lSubPartSize;
                    lNumSubParts++;
                }
            }

            
            while (stoppedThreads != numParts) //пока не выполнятся все потоки
            {
                for (int i = 0; i < numParts; i++)
                {
                    if (encr[i].ThreadState == System.Threading.ThreadState.Unstarted)
                    {
         
                        if (i != (numParts - 1) && ((occupiedMemory+partSize) < ramCounter.NextValue()))
                        {
         //                   GC.Collect();
                            encr[i].Start(i); Thread.Sleep(1000);
         //                   encr[i].Join();
                            unstartedThreads--;
                            runningThreads++;
                            occupiedMemory += partSize;
                        }
                        else if (i == (numParts - 1) && ((occupiedMemory+lastPartSize) < ramCounter.NextValue()))
                        {
                            encr[i].Start(i);

                            unstartedThreads--;
                            runningThreads++;
                            occupiedMemory -= lastPartSize;
                        }
                    }

                    if ((encr[i].ThreadState == System.Threading.ThreadState.Stopped) && (stoppedThreads < numParts))
                    {
               //         encr[i].Abort();
                        runningThreads--;
                        stoppedThreads++;
                        
    //                    if (i == (numParts - 1)) occupiedMemory -= lastPartSize;
                        
   //                     else occupiedMemory -= partSize;
  //                      Console.WriteLine("Thread {0} has ended.", i);
                    }
                }
                
            }
            
            //синхронизация потоков
            for(int i=0;i<numParts;i++)
            {
  //              encr[i].Start(i);
                encr[i].Join();
            }

            //           Thread.Sleep(1000);
 //           int count=0;
//            List<int> notWrote=new List<int>();
            
            for(int i=0;i<numParts;i++)
            {
                if (hash[i] != null)
                    Console.WriteLine("{0} : {1}", i, hash[i]);
                else counter1++;
                                                
            }
            Console.WriteLine(counter1);
 /*           foreach(int i in notWrote)
            {
                Console.WriteLine("Thread {0} didn't write hash", i);
            }

            foreach(int i in notWrote)
            {
                hashToDict(i);
                Console.WriteLine("{0} : {1}", i, hashParts[i]);
            }
            
            foreach(string str in hash)
            {
                if (str != null) counter2++;
            }
            Console.WriteLine(hash[notWrote[0]]);*/
        }

        public static string BytesToStr(byte[] bytes)
        {
            StringBuilder str = new StringBuilder();

            for (int i = 0; i < bytes.Length; i++)
                str.AppendFormat("{0:X2}", bytes[i]);

            return str.ToString();
        }

        public static int counter1 = 0, counter2 = 0, counter3 = 0;
        //каждый процесс вычисляет значение hash-функции для заданного номера блока
        public static void hashToDict(object _numPart)
        {
            _pool.WaitOne();
            int numPart = Convert.ToInt32(_numPart);
            using (FileStream fs = new FileStream(originalFile.Name, FileMode.Open, FileAccess.Read))
            {
                SHA256Managed sha256HashString = new SHA256Managed();
                
                //              Encoding enc8 = Encoding.ASCII;

                int hashOffset = 0;

                if (numPart != numParts) //если часть не последняя
                {
                    for(int i = 0; i < numSubParts; i++)
                    {
                        //                Console.WriteLine("numSub: {0}", numSubParts);

                        Console.WriteLine("seek: {0}", fs.Seek((numPart * partSize) + (i * subPartSize), SeekOrigin.Begin));
                        if (i != (numSubParts-1))
                        {
                            Console.WriteLine("i: {0}, thread: {1}, offset: {2}", i, numPart, hashOffset);
                            byte[] subPart = new byte[subPartSize];
                            fs.Read(subPart, 0, Convert.ToInt32(subPartSize));
                            
                            hashOffset += sha256HashString.TransformBlock(subPart, hashOffset, Convert.ToInt32(subPartSize), subPart, hashOffset);
                            Console.WriteLine("i: {0}, thread: {1}, offset: {2}", i, numPart, hashOffset);
                            subPart = null;
                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                        }
                        else if (i==(numSubParts-1))
                        {
                            
                            byte[] subPart = new byte[lastSubPartSize];
                            fs.Read(subPart, 0, Convert.ToInt32(lastSubPartSize));
                            Console.WriteLine("i: {0}, thread: {1}, offset: {2}, lastSize: {3}, sum: {4}", i, numPart, hashOffset, Convert.ToInt32(lastSubPartSize), subPart.Length);
                            try
                            {
                                sha256HashString.TransformFinalBlock(subPart, 0, Convert.ToInt32(lastSubPartSize));
                            }
                            catch(Exception e)
                            {
                                Console.WriteLine(e);
                                Console.WriteLine("StackTrace: {0}",e.StackTrace);
                            }
                            subPart = null;
                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                        }                        
                    }
                }
                else if (numPart==numParts)
                {
                    hashOffset = 0;
                    for (int i = 0; i < lNumSubParts; i++)
                    {
                        fs.Seek((numPart * partSize) + (i * lSubPartSize), SeekOrigin.Begin);
                        if (i != lNumSubParts)
                        {
                            byte[] subPart = new byte[lSubPartSize];
                            fs.Read(subPart, 0, Convert.ToInt32(lSubPartSize));
                            hashOffset += sha256HashString.TransformBlock(subPart, hashOffset, Convert.ToInt32(subPartSize), subPart, hashOffset);
                            subPart = null;
                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                        }
                        else if (i == (lNumSubParts-1))
                        {
                            byte[] subPart = new byte[lLastSubPartSize];
                            fs.Read(subPart, 0, Convert.ToInt32(lLastSubPartSize));
                            sha256HashString.TransformFinalBlock(subPart, 0, Convert.ToInt32(lLastSubPartSize));
                            subPart = null;
                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                        }
                    }
                }
                //           counter1++;

                hash[numPart] = BytesToStr(sha256HashString.Hash);
                Console.WriteLine("Thread {0} has ended", numPart);
  //              Thread.Sleep(500);
                
                
                /*
                //          byte[] partBytes = new byte[partSize]; //массив байт блока
                List<byte> blockFile = new List<byte>();

                MemoryStream ms = new MemoryStream();

                if (numPart == numParts) //если последний блок
                {
                    fs.Seek(numPart * lastPartSize, SeekOrigin.Begin);
                    for (long i = 0; i < lastPartSize; i++)
                    {
                        ms.WriteByte((byte)fs.ReadByte());
                    }

                }
                else
                {
                    fs.Seek(numPart * partSize, SeekOrigin.Begin);
                    for (long i = 0; i < partSize; i++)
                    {
                        ms.WriteByte((byte)fs.ReadByte());
                    }
                }
                
                byte[] hashBytes = new byte[sha256HashString.ComputeHash(ms).Length];
                hashBytes = sha256HashString.ComputeHash(ms); //вычисление значения hash-функции и запись в массив
                hashParts.Add(numPart, BitConverter.ToInt32(hashBytes, 0)); //добавление в словарь
    */
            }
            _pool.Release();
        }
    }
}