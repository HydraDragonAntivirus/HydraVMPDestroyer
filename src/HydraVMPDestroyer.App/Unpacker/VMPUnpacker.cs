using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using dnlib.DotNet;
using dnlib.PE;

namespace HydraVMPDestroyer.App.Unpacker
{
    public class VMPUnpacker
    {
        private const uint IMAGE_SCN_CNT_UNINITIALIZED_DATA = 0x00000080;
        private const int LZMA_PROPERTIES_SIZE = 5;

        public struct PackerInfo
        {
            public uint Src;
            public uint Dst;
        }

        public static byte[] Unpack(string filePath)
        {
            byte[] packedPeData = File.ReadAllBytes(filePath);
            return Unpack(packedPeData);
        }

        public static byte[] Unpack(byte[] packedPeData)
        {
            if (packedPeData == null || packedPeData.Length == 0)
                throw new Exception("Packed PE data is null or empty.");

            using (var pe = new PEImage(packedPeData))
            {
                uint sizeOfImage = pe.ImageNTHeaders.OptionalHeader.SizeOfImage;
                uint sizeOfHeaders = pe.ImageNTHeaders.OptionalHeader.SizeOfHeaders;

                byte[] unpackedImage = new byte[sizeOfImage];
                Array.Copy(packedPeData, unpackedImage, (int)sizeOfHeaders);

                // Align FileAlignment with SectionAlignment in the unpacked image
                uint sectionAlignment = pe.ImageNTHeaders.OptionalHeader.SectionAlignment;
                uint fileAlignmentOffset = (uint)pe.ImageNTHeaders.OptionalHeader.StartOffset + 36;
                if (fileAlignmentOffset + 4 <= sizeOfHeaders)
                {
                    byte[] saBytes = BitConverter.GetBytes(sectionAlignment);
                    Array.Copy(saBytes, 0, unpackedImage, (int)fileAlignmentOffset, 4);
                }

                var rvaPatternsArray = new List<byte[]>();
                foreach (var section in pe.ImageSectionHeaders)
                {
                    bool condition1 = section.SizeOfRawData == 0;
                    bool condition2 = section.PointerToRawData == 0;
                    bool condition3 = (section.Characteristics & IMAGE_SCN_CNT_UNINITIALIZED_DATA) == 0;

                    if (condition1 && condition2 && condition3)
                    {
                        ulong patternValue = ((ulong)section.VirtualAddress << 32) | 0xFFFFFFFF;
                        byte[] patternBytes = BitConverter.GetBytes(patternValue);
                        rvaPatternsArray.Add(patternBytes);
                    }
                }

                List<PackerInfo> packerInfoArray = new List<PackerInfo>();
                int numPackerEntries = 0;

                if (rvaPatternsArray.Count > 0)
                {
                    byte[] fullPattern = rvaPatternsArray.SelectMany(x => x).ToArray();
                    int patternPos = FindPattern(packedPeData, fullPattern);

                    if (patternPos != -1)
                    {
                        if (patternPos < 8)
                            throw new Exception("Located RVA pattern is too close to the beginning of the file to precede PACKER_INFO[0].");

                        int packerInfoOffset = patternPos - 8;
                        numPackerEntries = rvaPatternsArray.Count;

                        int endOfPackerInfoArray = packerInfoOffset + (numPackerEntries + 1) * 8;
                        if (endOfPackerInfoArray > packedPeData.Length || packerInfoOffset < 0)
                            throw new Exception("Located PACKER_INFO array extends beyond packed PE buffer or has invalid start.");

                        for (int j = 0; j <= numPackerEntries; j++)
                        {
                            int infoOffset = packerInfoOffset + j * 8;
                            uint src = BitConverter.ToUInt32(packedPeData, infoOffset);
                            uint dst = BitConverter.ToUInt32(packedPeData, infoOffset + 4);
                            packerInfoArray.Add(new PackerInfo { Src = src, Dst = dst });
                        }
                    }
                    else
                    {
                        Console.WriteLine("[WARNING] RVA pattern sequence for PACKER_INFO not found in packed PE, but patterns were expected.");
                        return null; // Signals fallback to MegaDumper
                    }
                }
                else
                {
                    Console.WriteLine("[INFO] RVA pattern array is empty. No PACKER_INFO entries to process for LZMA.");
                    return null; // Signals fallback to MegaDumper
                }

                // Copy existing sections and update headers
                for (int i = 0; i < pe.ImageSectionHeaders.Count; i++)
                {
                    var section = pe.ImageSectionHeaders[i];
                    uint virtualAddress = (uint)section.VirtualAddress;
                    uint virtualSize = section.VirtualSize;
                    uint sizeOfRawData = section.SizeOfRawData;
                    uint pointerToRawData = section.PointerToRawData;

                    if (pointerToRawData != 0 && sizeOfRawData > 0)
                    {
                        if (pointerToRawData + sizeOfRawData <= (uint)packedPeData.Length && virtualAddress + sizeOfRawData <= sizeOfImage)
                        {
                            Array.Copy(packedPeData, (int)pointerToRawData, unpackedImage, (int)virtualAddress, (int)sizeOfRawData);
                        }
                        else
                        {
                            Console.WriteLine($"[ERROR] Section {section.DisplayName} data exceeds boundaries. Skipping copy.");
                        }
                    }

                    // Update section header in unpackedImage
                    // PointerToRawData (offset 20) = VirtualAddress
                    // SizeOfRawData (offset 16) = VirtualSize (if > 0)
                    
                    uint sectionHeaderOffset = (uint)section.StartOffset;
                    if (sectionHeaderOffset != 0 && sectionHeaderOffset + 40 <= sizeOfHeaders)
                    {
                        byte[] vaBytes = BitConverter.GetBytes(virtualAddress);
                        Array.Copy(vaBytes, 0, unpackedImage, (int)sectionHeaderOffset + 20, 4);
                        
                        if (virtualSize > 0)
                        {
                            byte[] vsBytes = BitConverter.GetBytes(virtualSize);
                            Array.Copy(vsBytes, 0, unpackedImage, (int)sectionHeaderOffset + 16, 4);
                        }
                    }
                }

                // Process LZMA data
                if (packerInfoArray.Count > 1)
                {
                    PackerInfo propsInfo = packerInfoArray[0];
                    long propsFileOffsetLong = (long)pe.ToFileOffset((RVA)propsInfo.Src);

                    uint lzmaPropsSize = propsInfo.Dst;
                    byte[] lzmaPropsData = new byte[lzmaPropsSize];
                    Array.Copy(packedPeData, propsFileOffsetLong, lzmaPropsData, 0, (int)lzmaPropsSize);

                    for (int blockIdx = 1; blockIdx < packerInfoArray.Count; blockIdx++)
                    {
                        var currentBlockInfo = packerInfoArray[blockIdx];
                        uint compressedDataRva = currentBlockInfo.Src;
                        uint uncompressedTargetRva = currentBlockInfo.Dst;

                        uint compressedFileOffset = (uint)pe.ToFileOffset((RVA)compressedDataRva);
                        
                        // Decompress LZMA
                        byte[] decompressedData = DecompressLZMA(packedPeData, compressedFileOffset, lzmaPropsData);
                        
                        if (decompressedData != null)
                        {
                            uint availableSpace = sizeOfImage - uncompressedTargetRva;
                            int copyLength = Math.Min(decompressedData.Length, (int)availableSpace);
                            Array.Copy(decompressedData, 0, unpackedImage, (int)uncompressedTargetRva, copyLength);
                            
                            if (decompressedData.Length > availableSpace)
                                Console.WriteLine($"[ERROR] Block {blockIdx}: Decompressed data size exceeds available space");
                                
                            Console.WriteLine($"[INFO] Block {blockIdx}: Decompressed. Output size={decompressedData.Length}");
                        }
                    }
                }

                return unpackedImage;
            }
        }

        private static int FindPattern(byte[] data, byte[] pattern)
        {
            if (pattern == null || data.Length < pattern.Length) return -1;

            for (int i = 0; i <= data.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (pattern[j] != 0xFF && data[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }

        private static byte[] DecompressLZMA(byte[] data, uint offset, byte[] props)
        {
            try
            {
                using (var ms = new MemoryStream(data, (int)offset, data.Length - (int)offset))
                using (var outMs = new MemoryStream())
                {
                    var decoder = new SevenZip.Compression.LZMA.Decoder();
                    decoder.SetDecoderProperties(props);

                    // Since we don't know the uncompressed size, we'll try a large value.
                    // VMP blocks are usually not huge, but let's use a safe limit or the image size.
                    long outSize = 1024 * 1024 * 10; // 10MB limit for a single block? 
                    // Better: use the remaining image size.
                    
                    try {
                        decoder.Code(ms, outMs, -1, outSize, null);
                    } catch (Exception) {
                        // Some decoders throw at the end of stream if outSize is not reached.
                    }

                    return outMs.ToArray();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] LZMA Decompression failed: {ex.Message}");
                return null;
            }
        }
    }
}
