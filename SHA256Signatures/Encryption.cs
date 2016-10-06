using System;
using System.Diagnostics;
using System.Text;
using System.Security.Cryptography;
using System.IO;
using System.Threading;
using System.Collections.Generic;

namespace SHA256Signatures
{
    class Encryption
    {
        static FileInfo originalFile;
        static long partSize = 0; //размер блока
        static long lastPartSize = 0; //размер последнего блока
        static long numParts = 0; //количество блоков
        static bool isLastPart = false; //флаг последнего блока
        static int lastPartNumber = 0; //индекс последнего блока
        static string[] hash; //массив строк значений хэш-функции для каждого блока
        int maxThreads = 0; //максимальное количество потоков, которые могут быть запущены одновременно
        Process currentProcess = Process.GetCurrentProcess();
        static Queue<byte[]> partQueue = new Queue<byte[]>();
        static Mutex queueMutex = new Mutex();
        static Semaphore sem = new Semaphore(1, 1);


        public Encryption(string _filename, long _partSize)
        {
            try
            {
                originalFile = new FileInfo(@_filename);
                partSize = _partSize;

                numParts = originalFile.Length / partSize; //количество частей
                lastPartSize = originalFile.Length % partSize; //размер последней части

                if (lastPartSize != 0)
                {
                    numParts++;
                }
                else if (lastPartSize == 0)
                {
                    lastPartSize = partSize;
                }

                lastPartNumber = (int)numParts - 1;

                hash = new string[numParts];
            }
            catch (FileNotFoundException fe)
            {
                Console.WriteLine("Ошибка: {0}\nStackTrace: {1}", fe.Message, fe.StackTrace);
                return;
            }
            catch (Exception e)
            {
                Console.WriteLine("Ошибка: {0}\nStackTrace: {1}", e.Message, e.StackTrace);
            }
        }

        private int maxWorkingSetCount()
        {
            try
            {
                PerformanceCounter ramCounter = new PerformanceCounter("Memory", "Available Bytes");
                double availableRAM = ramCounter.NextValue(); //подсчет свободной оперативной памяти

                //если размер части больше доступной оперативной памяти
                if (partSize > availableRAM)
                {
                    Console.WriteLine("Размер блока меньше доступной оперативной памяти.\nВ данный момент доступно: {0} байт.", availableRAM);
                    return 1;
                }

                //задание максимального размера рабочего пространства для текущего процесса
                if (int.MaxValue < availableRAM)
                {
                    currentProcess.MaxWorkingSet = (IntPtr)(int.MaxValue);
                }
                else if (int.MaxValue >= availableRAM)
                {
                    currentProcess.MaxWorkingSet = (IntPtr)availableRAM;
                }
                //подсчет количества потоков, которые могут быть запущены одновременно
                maxThreads = (int)currentProcess.MaxWorkingSet / (int)partSize;

                Console.WriteLine("Peak: {0}", currentProcess.PeakWorkingSet64);

                return 0;
            }
            catch (Exception e)
            {
                Console.WriteLine("Ошибка: {0}\nStackTrace: {1}", e.Message, e.StackTrace);
                return 1;
            }
        }           

        private void readFromFile()
        {
            using (FileStream fs = new FileStream(originalFile.FullName, FileMode.Open, FileAccess.Read))
            {
                try
                {
                    byte[] part=null;
                    for (int i = 0; i < numParts; i++)
                    {
                        //                     queueMutex.WaitOne();
                        sem.WaitOne();
                        while (partQueue.Count != 0) { }
                        long len = 0;

                        if (i == lastPartNumber)
                        {
                            len = lastPartSize;
                        }
                        else
                        {
                            len = partSize;
                        }

                        /*            using(MemoryStream ms=new MemoryStream())
                                    {
                                        fs.Seek(i * partSize, SeekOrigin.Begin);

                                        for(int j = 0; j < len; j++)
                                        {
                                            ms.WriteByte((byte)fs.ReadByte());
                                        }
                                        partQueue.Enqueue(ms.ToArray());
                                    }*/

                        part = new byte[len];

                        fs.Read(part, 0, (int)len);
                        
                        partQueue.Enqueue(part);
                        
                        //                  GC.Collect(1,GCCollectionMode.Optimized);
                        //                 GC.WaitForPendingFinalizers();

                        part = null;

                        GC.Collect(2, GCCollectionMode.Forced);

                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                        sem.Release();
                        //                    queueMutex.ReleaseMutex();
                        //             while (partQueue.Count != 0) { }
                    }
                }
                catch(Exception e)
                {
                    Console.WriteLine("Ошибка: {0}\nStackTrace: {1}", e.Message, e.StackTrace);
                }
            } 
        }

        //каждый процесс вычисляет значение hash-функции для заданного номера блока
        private static void hashToArray()
        {
            try
            {
                SHA256Managed sha256HashString = new SHA256Managed(); //переменная для хэширования массива байт
                int numPart = 0;
                byte[] part;

                while (numPart < numParts)
                {
                    while (partQueue.Count != 1) { }

                    int len1 = partQueue.Peek().GetLength(0);

                    sem.WaitOne();

                    Console.WriteLine("Блок {0} обрабатывается", numPart);
                    Console.WriteLine("queue: {0},lastpart: {1}", len1, (int)lastPartSize);

                    //               queueMutex.WaitOne();

                    long len = 0;
                    if (numPart == lastPartNumber)
                    {
                        len = lastPartSize;
                    }
                    else
                    {
                        len = partSize;
                    }

                    //              part = new byte[len];
                    part = partQueue.Dequeue(); //массив байт блока


                    hash[numPart] = BytesToStr(sha256HashString.ComputeHash(part)); //запись значения хэш-функции для блока numPart

                    //               queueMutex.ReleaseMutex();

                    GC.Collect(2, GCCollectionMode.Forced);

                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    sem.Release();
                    Console.WriteLine("Блок {0} записан", numPart);
                    numPart++;

                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Ошибка: {0}\nStackTrace: {1}", e.Message, e.StackTrace);
            }
        }

        public void threadsControl()
        {
            try
            {
                currentProcess.PriorityClass = ProcessPriorityClass.High; //выставление максимального приоритета текущему процесса

                int i = maxWorkingSetCount();

                if (i == 1)
                {
                    return;
                }

                Thread readingThread = new Thread(readFromFile);
                Thread calculateThread = new Thread(hashToArray);

                readingThread.Start();
                calculateThread.Start();

                readingThread.Join();
                calculateThread.Join();

                hashWrite();

            }
            catch (Exception e)
            {
                Console.WriteLine("Ошибка: {0}\nStackTrace: {1}", e.Message, e.StackTrace);
            }
        }

        public void threadsControl1()
        {
            try
            {
                currentProcess.PriorityClass = ProcessPriorityClass.High; //выставление максимального приоритета текущему процесса

                if (maxWorkingSetCount() == 1)
                {
                    return;
                }

                int occupiedMemory = 0; //счетчик занятой оперативной памяти потоками

                int[] stoppedThreads = new int[numParts]; //массив флагов выполненных потоков
                Array.Clear(stoppedThreads, 0, stoppedThreads.Length);

                Thread[] encr = new Thread[numParts]; //создание массива потоков

                //инициализация массива потоков
                for (int i = 0; i < numParts; i++)
                {
                    //каждый поток будет выполнять процедуру hashToDict с параметром
                    encr[i] = new Thread(new ParameterizedThreadStart(hashToDict));
                }

                bool flag = false; //флаг выполнения всех потоков

                while (flag != true) //пока не выполнятся все потоки
                {
                    //за одну итерацию проверяются все потоки
                    for (int i = 0; i < numParts; i++)
                    {
                        //если установлен флаг и последний поток еще не был запущен
                        if (isLastPart && encr[lastPartNumber].ThreadState == System.Threading.ThreadState.Unstarted)
                        {
                            encr[lastPartNumber].Start(lastPartNumber); //запуск потока
                            occupiedMemory += (int)lastPartSize; //увеличение счетчика занятой памяти на размер последнего блока
                        }
                        //если поток еще не был запущен
                        else if (encr[i].ThreadState == System.Threading.ThreadState.Unstarted)
                        {
                            //если непоследний поток и если после его запуска размер всех запущенных потоков не будет превышать максимальный размер рабочего пространства
                            if ((i != lastPartNumber) && ((occupiedMemory + partSize) < (int)currentProcess.MaxWorkingSet))
                            {
                                encr[i].Start(i); //запуск потока
                                occupiedMemory += (int)partSize; //увеличение счетчика занятой памяти на размер блока
                            }
                            //если непоследний поток и если после его запуска размер всех запущенных потоков не будет превышать максимальный размер рабочего пространства
                            else if ((i == lastPartNumber) && ((occupiedMemory + lastPartSize) < (int)currentProcess.MaxWorkingSet))
                            {
                                encr[i].Start(i); //запуск потока
                                occupiedMemory += (int)lastPartSize; //увеличение счетчика занятой памяти на размер последнего блока блока
                            }
                        }
                        //если поток выполнен
                        else if ((encr[i].ThreadState == System.Threading.ThreadState.Stopped))
                        {
                            stoppedThreads[i] = 1; //установка флага о том что поток с текущим номером выполнен
                            //если последний поток
                            if (i == (lastPartNumber))
                            {
                                occupiedMemory -= (int)lastPartSize; //уменьшение счетчика занятой памяти на размер последнего блока блока
                            }
                            //иначе
                            else
                            {
                                occupiedMemory -= (int)partSize; //уменьшение счетчика занятой памяти на размер блока
                            }
                        }
                    }

                    //подсчет количества выполненных потоков
                    int countStoppedThreads = 0;
                    for (int i = 0; i < numParts; i++)
                    {
                        //если поток остановлен
                        if (stoppedThreads[i] == 1)
                        {
                            countStoppedThreads++; //инкремент счетчика
                        }
                    }

                    //если все потоки выполнены
                    if (countStoppedThreads == numParts)
                    {
                        flag = true;
                    }
                }

                hashWrite(); //вывод значений хэш-функции всех частей
            }
            catch (Exception e)
            {
                Console.WriteLine("Ошибка: {0}\nStackTrace: {1}", e.Message, e.StackTrace);
            }
        }

        private void hashWrite()
        {
            try
            {
                Console.WriteLine("\nЗначения хэш-функции SHA256:\n");                
                for (int i = 0; i < numParts; i++)
                {
                    Console.WriteLine("{0} : {1}", i, hash[i]);
                }
            }
            catch(Exception e)
            {
                Console.WriteLine("Ошибка: {0}\nStackTrace: {1}", e.Message, e.StackTrace);
            }
        }

        //перевод массива байт в строку
        private static string BytesToStr(byte[] bytes)
        {
            try
            {
                StringBuilder str = new StringBuilder();

                for (int i = 0; i < bytes.Length; i++)
                    str.AppendFormat("{0:X2}", bytes[i]);

                return str.ToString();
            }
            catch (Exception e)
            {
                Console.WriteLine("Ошибка: {0}\nStackTrace: {1}", e.Message, e.StackTrace);
                return "";
            }
        }

        

        //каждый процесс вычисляет значение hash-функции для заданного номера блока
        private static void hashToDict(object _numPart)
        {
     //       pool.WaitOne(); //блокировка текущего потока
            int numPart = Convert.ToInt32(_numPart);
            try
            {
                using (FileStream fs = new FileStream(originalFile.FullName, FileMode.Open, FileAccess.Read))
                {
                    SHA256Managed sha256HashString = new SHA256Managed(); //переменная для хэширования массива байт

                    Console.WriteLine("Поток {0} запущен", numPart);
                    byte[] part; //массив байт блока

                    if (numPart == (lastPartNumber)) //если последний блок
                    {
                        part = new byte[lastPartSize];
                        fs.Seek(numPart * partSize, SeekOrigin.Begin); //установка указателя в файле, который определяется как: номер_блока*размер_блока
                        fs.Read(part, 0, (int)lastPartSize); //запись в массив part указанного количества байт
                    }
                    else //иначе
                    {
                        part = new byte[partSize];
                        fs.Seek(numPart * partSize, SeekOrigin.Begin); //установка указателя в файле, который определяется как: номер_блока*размер_блока
                        fs.Read(part, 0, (int)partSize); //запись в массив part указанного количества байт
                    }

                    hash[numPart] = BytesToStr(sha256HashString.ComputeHash(part)); //запись значения хэш-функции для блока numPart
                    part = null; //обнуление массива

                    Console.WriteLine("Поток {0} завершен", numPart);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Поток {0}\nОшибка: {1}\nStackTrace: {2}", numPart, e.Message, e.StackTrace);
            }
     //       pool.Release(); //выход из семафора
        }
    }
}