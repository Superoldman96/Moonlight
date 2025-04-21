﻿using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Security.Cryptography;
using Moonlight;

namespace MoonlightLauncher
{
    public struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    public struct STARTUPINFO
    {
        public uint cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public uint dwX;
        public uint dwY;
        public uint dwXSize;
        public uint dwYSize;
        public uint dwXCountChars;
        public uint dwYCountChars;
        public uint dwFillAttribute;
        public uint dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    public struct SECURITY_ATTRIBUTES
    {
        public int length;
        public IntPtr lpSecurityDescriptor;
        public bool bInheritHandle;
    }

    public partial class Form1 : Form
    {
        /**
         * DLL Imports
         */
        [DllImport("kernel32.dll")]
        static extern bool CreateProcess(string lpApplicationName, string lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
                        bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment,
                        string lpCurrentDirectory, ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern uint ResumeThread(IntPtr hThread);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint dwDesiredAccess, int bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern int CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, IntPtr dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern int WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] buffer, uint size, int lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttribute, IntPtr dwStackSize, IntPtr lpStartAddress,
            IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

        /**
         * Class member variables.
         */
         private readonly Client client;

        private readonly static uint CREATE_SUSPENDED = 0x00000004;
        private readonly static String sDllPath = "Moonlight.dll";

        public Form1()
        {
            InitializeComponent();

            this.client = new Client();
            this.client.Connect();
        }

        private void LoginButton_Click(object sender, EventArgs e)
        {
            if ( !this.client.isConnected ) {
                MessageBox.Show("Unable to connect to the server. Please try again later.", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return;
            }

            LaunchMapleAsync();
        }

        private static int GetProcessId(String proc)
        {
            int processID = -1;

            Process[] processes = Process.GetProcesses();
            for (int i = 0; i < processes.Length; i++)
            {
                if (processes[i].ProcessName == proc)
                {
                    processID = (int)processes[i].Id;

                    break;
                }
            }

            return processID;
        }

        private static int Inject(String exe, String dllPath)
        {
            int processID = GetProcessId(exe);
            if (processID == -1)
            {
                return 1;
            }

            IntPtr pLoadLibraryAddress = GetProcAddress(GetModuleHandle("Kernel32.dll"), "LoadLibraryA");
            if (pLoadLibraryAddress == (IntPtr)0)
            {
                return 2;
            }

            IntPtr processHandle = OpenProcess((0x2 | 0x8 | 0x10 | 0x20 | 0x400), 1, (uint)processID);
            if (processHandle == (IntPtr)0)
            {
                return 3;
            }

            IntPtr lpAddress = VirtualAllocEx(processHandle, (IntPtr)null, (IntPtr)dllPath.Length, (0x1000 | 0x2000), 0X40);
            if (lpAddress == (IntPtr)0)
            {
                return 4;
            }

            byte[] bytes = Encoding.ASCII.GetBytes(dllPath);
            if (WriteProcessMemory(processHandle, lpAddress, bytes, (uint)bytes.Length, 0) == 0)
            {
                return 5;
            }

            if (CreateRemoteThread(processHandle, (IntPtr)null, (IntPtr)0, pLoadLibraryAddress, lpAddress, 0, (IntPtr)null) == (IntPtr)0)
            {
                return 6;
            }

            CloseHandle(processHandle);

            return 0;
        }

        /**
         * Perform a file integrity check on the provided file.
         */
        private async Task<bool> VerifyFileIntegrityAsync(string wzFile, string[] wzIgnore)
        {
            if (wzIgnore.Any(s => wzFile.Contains(s)))
                return true;

            string[] fileParts = wzFile.Split('\\');
            string fileName = fileParts[fileParts.Length - 1].Replace(".wz", "");

            FileInfo fileInfo = new FileInfo(wzFile);
            long fileSize = fileInfo.Length;

            byte[] partialHash;
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                using (var stream = new FileStream(wzFile, FileMode.Open, FileAccess.Read))
                {
                    byte[] buffer = new byte[8192]; // Read 8 KB chunks
                    int bytesRead;

                    // Read the first 8 KB
                    bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    byte[] firstPart = buffer.Take(bytesRead).ToArray();

                    // Read the last 8 KB
                    stream.Seek(-Math.Min(buffer.Length, fileSize), SeekOrigin.End);
                    bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    byte[] lastPart = buffer.Take(bytesRead).ToArray();

                    // Combine first and last parts for hashing
                    partialHash = md5.ComputeHash(firstPart.Concat(lastPart).ToArray());
                }
            }

            string hashString = BitConverter.ToString(partialHash).Replace("-", "").ToLowerInvariant();

            byte checkFileChecksum = await this.GetFileChecksum(fileName, hashString, fileSize);

            if (checkFileChecksum.Equals(1))
            {
                MessageBox.Show("One or more .wz files failed the integrity check.", "Integrity Error", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return false;
            }

            return true;
        }

        /**
         * Attempt to launch the client.
         * - Performs CRC32 checks on core .wz files
         */
        async Task<bool> LaunchMapleAsync()
        {
            string username = this.usernameTextBox.Text;
            string password = this.passwordTextBox.Text;

            this.passwordTextBox.Clear();

            string token = await this.GetToken(username, password);

            bool flag = !token.Equals("");

            if ( string.IsNullOrEmpty(token) )
            {
                MessageBox.Show("Invalid username/password combination.");
                return flag;
            }

            // Don't need to checksum these files.
            string[] wz_ignore = { "Effect", "Sound", "Morph", "Reactor", "String", "TamingMob", "Base" };

            // Get all .wz files.
            string[] wz_files = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.wz", SearchOption.TopDirectoryOnly);
            if ( wz_files.Length != 25 ) {
                MessageBox.Show("Unable to find the client's required .wz files.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return false;
            }

            // Perform CRC32 checks on all .wz files.
            bool allFilesValid = true;
            await Task.Run(() =>
            {
                Parallel.ForEach(wz_files, async wz_file =>
                {
                    if (!await VerifyFileIntegrityAsync(wz_file, wz_ignore))
                    {
                        allFilesValid = false;
                        return;
                    }
                });
            });

            // One or more .wz file failed the integrity check.
            if ( !allFilesValid )
            {
                MessageBox.Show("One or more .wz files failed the integrity check.", "Integrity Error", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return false;
            }

            // All .wz files passed the integrity check.
            // Attempt to launch the game.
            // Launch the game process as before
            try
            {
                STARTUPINFO si = new STARTUPINFO();
                PROCESS_INFORMATION pi = new PROCESS_INFORMATION();

                bool bCreateProc = CreateProcess("MapleStory.exe", $" WebStart {token}", IntPtr.Zero, IntPtr.Zero, false, CREATE_SUSPENDED, IntPtr.Zero, null, ref si, out pi);

                if (bCreateProc)
                {
                    int bInject = Inject("MapleStory", sDllPath);
                    if (bInject == 0)
                    {
                        ResumeThread(pi.hThread);

                        CloseHandle(pi.hThread);
                        CloseHandle(pi.hProcess);
                    }
                    else
                    {
                        MessageBox.Show("Error code: " + bInject.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to launch Moonlight. Please make sure that the launcher file is in your game folder and that this program is ran with Administator privledges.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Hand);
            }

            return true;
        }

        public void Run()
        {
            LaunchMapleAsync();
        }

        private async Task<byte> GetFileChecksum(string filename, string checksum, long filesize)
        {
            this.client.Send(OutPackets.FileChecksum(filename, checksum, filesize));

            InPacket inPacket = this.client.Receive();
            inPacket.readInt();

            int num = (int)inPacket.readShort();

            return inPacket.readByte();
        }

        private async Task<string> GetToken(string username, string password)
        {
            this.client.Send(OutPackets.AuthRequest(username, password));
            return Handlers.getAuthTokenFromInput(this.client.Receive());
        }

        private void CreateAccountButton_Click(object sender, EventArgs e)
        {
            new Form2() { client = this.client }.Show();
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }
	  }
}
