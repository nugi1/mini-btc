using System.Buffers.Binary;

namespace MiniBtc.Domain.Scripts;

/// <summary>Reads scripts that consist only of data pushes, the form a standard scriptSig takes.</summary>
public static class ScriptParser
{
    /// <summary>script/script.h MAX_SCRIPT_ELEMENT_SIZE: no single push may exceed 520 bytes.</summary>
    public const int MaxElementSize = 520;

    private const byte OpPushData1 = 0x4c;
    private const byte OpPushData2 = 0x4d;
    private const byte OpPushData4 = 0x4e;

    /// <summary>
    /// Splits a push-only script into the byte strings it pushes. Opcodes 0x00–0x4b push that many
    /// bytes (0x00 = OP_0, an empty push); OP_PUSHDATA1/2/4 carry a 1-, 2- or 4-byte little-endian
    /// length first. False for any other opcode or a push that runs past the end.
    /// </summary>
    public static bool TryParsePushes(ReadOnlySpan<byte> script, out List<byte[]> pushes)
    {
        pushes = [];
        var position = 0;
        while (position < script.Length)
        {
            var opcode = script[position++];
            int length;
            if (opcode < OpPushData1)
            {
                length = opcode;
            }
            else if (opcode == OpPushData1 && position + 1 <= script.Length)
            {
                length = script[position];
                position += 1;
            }
            else if (opcode == OpPushData2 && position + 2 <= script.Length)
            {
                length = BinaryPrimitives.ReadUInt16LittleEndian(script[position..]);
                position += 2;
            }
            else if (opcode == OpPushData4 && position + 4 <= script.Length)
            {
                var declared = BinaryPrimitives.ReadUInt32LittleEndian(script[position..]);
                if (declared > int.MaxValue) return false;
                length = (int)declared;
                position += 4;
            }
            else
            {
                return false;
            }

            if (length > script.Length - position) return false;
            pushes.Add(script.Slice(position, length).ToArray());
            position += length;
        }
        return true;
    }
}
