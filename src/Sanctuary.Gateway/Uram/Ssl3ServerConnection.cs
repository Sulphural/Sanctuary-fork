using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sanctuary.Gateway.Uram;

public sealed class Ssl3Exception(string message) : Exception(message);

public sealed class Ssl3ServerConnection : IDisposable
{
    private const byte ContentChangeCipherSpec = 20;
    private const byte ContentAlert = 21;
    private const byte ContentHandshake = 22;
    private const byte ContentApplicationData = 23;

    private const byte HandshakeClientHello = 1;
    private const byte HandshakeServerHello = 2;
    private const byte HandshakeCertificate = 11;
    private const byte HandshakeServerHelloDone = 14;
    private const byte HandshakeClientKeyExchange = 16;
    private const byte HandshakeFinished = 20;

    private const int MaxRecordLength = 16384 + 2048;

    private static readonly byte[] ClientSender = [0x43, 0x4C, 0x4E, 0x54];
    private static readonly byte[] ServerSender = [0x53, 0x52, 0x56, 0x52];

    private sealed record CipherSuite(ushort Id, string Algorithm, int KeyLength, int BlockSize);

    private static readonly CipherSuite[] SupportedSuites =
    [
        new(0x002F, "AES", 16, 16),
        new(0x0035, "AES", 32, 16),
        new(0x000A, "3DES", 24, 8)
    ];

    private sealed class DirectionState
    {
        public required CipherSuite Suite;
        public required byte[] MacSecret;
        public required SymmetricAlgorithm Cipher;
        public required byte[] Iv;
        public ulong Sequence;
    }

    private readonly Stream _stream;
    private readonly RSA _privateKey;
    private readonly byte[][] _certificateChain;
    private readonly MemoryStream _transcript = new();
    private readonly List<byte> _handshakeBuffer = [];
    private readonly Queue<byte> _applicationBuffer = new();

    private DirectionState? _readState;
    private DirectionState? _writeState;
    private bool _closed;

    public Ssl3ServerConnection(Stream stream, X509Certificate2 certificate, IEnumerable<X509Certificate2> extraChain)
    {
        _stream = stream;
        _privateKey = certificate.GetRSAPrivateKey() ?? throw new ArgumentException("The certificate needs an RSA private key.");
        _certificateChain = new[] { certificate.RawData }.Concat(extraChain.Select(x => x.RawData)).ToArray();
    }

    public void Handshake()
    {
        var clientHello = ReadHandshakeMessage(HandshakeClientHello);
        var (clientRandom, suite) = ParseClientHello(clientHello);

        var serverRandom = RandomNumberGenerator.GetBytes(32);
        var sessionId = RandomNumberGenerator.GetBytes(32);

        var serverHello = new List<byte> { 0x03, 0x00 };
        serverHello.AddRange(serverRandom);
        serverHello.Add((byte)sessionId.Length);
        serverHello.AddRange(sessionId);
        serverHello.Add((byte)(suite.Id >> 8));
        serverHello.Add((byte)suite.Id);
        serverHello.Add(0);

        var certificates = new List<byte>();
        foreach (var der in _certificateChain)
        {
            AddUInt24(certificates, der.Length);
            certificates.AddRange(der);
        }

        var certificateBody = new List<byte>();
        AddUInt24(certificateBody, certificates.Count);
        certificateBody.AddRange(certificates);

        var flight = new List<byte>();
        flight.AddRange(HandshakeMessage(HandshakeServerHello, serverHello.ToArray()));
        flight.AddRange(HandshakeMessage(HandshakeCertificate, certificateBody.ToArray()));
        flight.AddRange(HandshakeMessage(HandshakeServerHelloDone, []));
        WriteRecord(ContentHandshake, flight.ToArray());

        var keyExchange = ReadHandshakeMessage(HandshakeClientKeyExchange);
        var preMaster = DecryptPreMaster(keyExchange);

        var masterSecret = DeriveBlock(preMaster, clientRandom, serverRandom, 48);
        var keyBlockLength = 2 * (20 + suite.KeyLength + suite.BlockSize);
        var keyBlock = DeriveBlock(masterSecret, serverRandom, clientRandom, keyBlockLength);

        var offset = 0;
        byte[] Take(int count)
        {
            var part = keyBlock.AsSpan(offset, count).ToArray();
            offset += count;
            return part;
        }

        var clientMac = Take(20);
        var serverMac = Take(20);
        var clientKey = Take(suite.KeyLength);
        var serverKey = Take(suite.KeyLength);
        var clientIv = Take(suite.BlockSize);
        var serverIv = Take(suite.BlockSize);

        var record = ReadRecord();
        if (record.Type != ContentChangeCipherSpec || record.Data.Length != 1 || record.Data[0] != 1)
            throw new Ssl3Exception("Expected ChangeCipherSpec.");

        _readState = new DirectionState { Suite = suite, MacSecret = clientMac, Cipher = CreateCipher(suite, clientKey), Iv = clientIv };

        var expectedClientFinished = FinishedHash(masterSecret, ClientSender);
        var clientFinished = ReadHandshakeMessage(HandshakeFinished, includeInTranscript: false);

        if (!CryptographicOperations.FixedTimeEquals(clientFinished, expectedClientFinished))
            throw new Ssl3Exception("Client Finished did not verify.");

        HandshakeMessage(HandshakeFinished, clientFinished);

        WriteRecord(ContentChangeCipherSpec, [1]);
        _writeState = new DirectionState { Suite = suite, MacSecret = serverMac, Cipher = CreateCipher(suite, serverKey), Iv = serverIv };

        var serverFinished = FinishedHash(masterSecret, ServerSender);
        WriteRecord(ContentHandshake, HandshakeMessage(HandshakeFinished, serverFinished, includeInTranscript: false));
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        while (_applicationBuffer.Count == 0)
        {
            if (_closed)
                return 0;

            var record = ReadRecord();

            if (record.Type == ContentApplicationData)
            {
                foreach (var b in record.Data)
                    _applicationBuffer.Enqueue(b);
            }
            else if (record.Type == ContentAlert)
            {
                _closed = true;
                return 0;
            }
            else
            {
                throw new Ssl3Exception($"Unexpected record type {record.Type} after the handshake.");
            }
        }

        var read = 0;
        while (read < count && _applicationBuffer.Count > 0)
            buffer[offset + read++] = _applicationBuffer.Dequeue();

        return read;
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        while (data.Length > 0)
        {
            var chunk = data[..Math.Min(data.Length, 16384)];
            WriteRecord(ContentApplicationData, chunk.ToArray());
            data = data[chunk.Length..];
        }
    }

    public void Close()
    {
        try
        {
            if (_writeState is not null)
                WriteRecord(ContentAlert, [1, 0]);
        }
        catch (IOException)
        {
        }
    }

    public void Dispose()
    {
        _readState?.Cipher.Dispose();
        _writeState?.Cipher.Dispose();
        _privateKey.Dispose();
    }

    private static SymmetricAlgorithm CreateCipher(CipherSuite suite, byte[] key)
    {
        SymmetricAlgorithm cipher = suite.Algorithm == "AES" ? Aes.Create() : TripleDES.Create();
        cipher.Key = key;
        return cipher;
    }

    private static (byte[] ClientRandom, CipherSuite Suite) ParseClientHello(byte[] body)
    {
        var reader = new SpanReader(body);
        var major = reader.Byte();
        var minor = reader.Byte();

        if (major != 3)
            throw new Ssl3Exception($"Unsupported client version {major}.{minor}.");

        var clientRandom = reader.Bytes(32);
        reader.Bytes(reader.Byte());

        var suiteBytes = reader.Bytes(reader.UInt16());
        var offered = new HashSet<ushort>();
        for (var i = 0; i + 1 < suiteBytes.Length; i += 2)
            offered.Add((ushort)((suiteBytes[i] << 8) | suiteBytes[i + 1]));

        var suite = SupportedSuites.FirstOrDefault(x => offered.Contains(x.Id))
            ?? throw new Ssl3Exception("No shared cipher suite. Offered: " + string.Join(",", offered.Select(x => x.ToString("X4"))));

        return (clientRandom, suite);
    }

    private byte[] DecryptPreMaster(byte[] body)
    {
        var modulusLength = _privateKey.KeySize / 8;
        var encrypted = body;

        if (body.Length == modulusLength + 2 && ((body[0] << 8) | body[1]) == modulusLength)
            encrypted = body[2..];

        byte[] preMaster;
        try
        {
            preMaster = _privateKey.Decrypt(encrypted, RSAEncryptionPadding.Pkcs1);
        }
        catch (CryptographicException)
        {
            preMaster = RandomNumberGenerator.GetBytes(48);
        }

        if (preMaster.Length != 48)
            preMaster = RandomNumberGenerator.GetBytes(48);

        return preMaster;
    }

    private static byte[] DeriveBlock(byte[] secret, byte[] randomA, byte[] randomB, int length)
    {
        var output = new List<byte>();

        for (var i = 1; output.Count < length; i++)
        {
            var label = Enumerable.Repeat((byte)('A' + i - 1), i).ToArray();
            var sha = SHA1.HashData([.. label, .. secret, .. randomA, .. randomB]);
            output.AddRange(MD5.HashData([.. secret, .. sha]));
        }

        return output.Take(length).ToArray();
    }

    private byte[] FinishedHash(byte[] masterSecret, byte[] sender)
    {
        var transcript = _transcript.ToArray();

        var md5Inner = MD5.HashData([.. transcript, .. sender, .. masterSecret, .. Pad(0x36, 48)]);
        var md5 = MD5.HashData([.. masterSecret, .. Pad(0x5C, 48), .. md5Inner]);

        var shaInner = SHA1.HashData([.. transcript, .. sender, .. masterSecret, .. Pad(0x36, 40)]);
        var sha = SHA1.HashData([.. masterSecret, .. Pad(0x5C, 40), .. shaInner]);

        return [.. md5, .. sha];
    }

    private static byte[] Pad(byte value, int count) => Enumerable.Repeat(value, count).ToArray();

    private static byte[] ComputeMac(DirectionState state, byte type, ReadOnlySpan<byte> content)
    {
        var header = new byte[11];
        for (var i = 0; i < 8; i++)
            header[i] = (byte)(state.Sequence >> (56 - 8 * i));
        header[8] = type;
        header[9] = (byte)(content.Length >> 8);
        header[10] = (byte)content.Length;

        var inner = SHA1.HashData([.. state.MacSecret, .. Pad(0x36, 40), .. header, .. content]);
        return SHA1.HashData([.. state.MacSecret, .. Pad(0x5C, 40), .. inner]);
    }

    private byte[] HandshakeMessage(byte type, byte[] body, bool includeInTranscript = true)
    {
        var message = new byte[4 + body.Length];
        message[0] = type;
        message[1] = (byte)(body.Length >> 16);
        message[2] = (byte)(body.Length >> 8);
        message[3] = (byte)body.Length;
        body.CopyTo(message, 4);

        if (includeInTranscript)
            AppendTranscript(message);

        return message;
    }

    private void AppendTranscript(byte[] message) => _transcript.Write(message);

    private byte[] ReadHandshakeMessage(byte expectedType, bool includeInTranscript = true)
    {
        while (true)
        {
            if (_handshakeBuffer.Count >= 4)
            {
                var length = (_handshakeBuffer[1] << 16) | (_handshakeBuffer[2] << 8) | _handshakeBuffer[3];

                if (_handshakeBuffer.Count >= 4 + length)
                {
                    var message = _handshakeBuffer.Take(4 + length).ToArray();
                    _handshakeBuffer.RemoveRange(0, 4 + length);

                    if (message[0] != expectedType)
                        throw new Ssl3Exception($"Expected handshake message {expectedType}, got {message[0]}.");

                    if (includeInTranscript)
                        AppendTranscript(message);

                    return message[4..];
                }
            }

            var record = ReadRecord();

            if (record.Type == ContentAlert)
                throw new Ssl3Exception($"Client sent alert {string.Join(",", record.Data)} during the handshake.");

            if (record.Type != ContentHandshake)
                throw new Ssl3Exception($"Expected a handshake record, got {record.Type}.");

            _handshakeBuffer.AddRange(record.Data);
        }
    }

    private (byte Type, byte[] Data) ReadRecord()
    {
        var header = ReadExactly(5);
        var type = header[0];

        if (header[1] != 3)
            throw new Ssl3Exception($"Unexpected record version {header[1]}.{header[2]}.");

        var length = (header[3] << 8) | header[4];
        if (length > MaxRecordLength)
            throw new Ssl3Exception("Record too long.");

        var data = ReadExactly(length);

        if (_readState is null)
            return (type, data);

        var state = _readState;
        if (data.Length == 0 || data.Length % state.Suite.BlockSize != 0)
            throw new Ssl3Exception("Bad encrypted record length.");

        var plain = state.Cipher.DecryptCbc(data, state.Iv, PaddingMode.None);
        state.Iv = data[^state.Suite.BlockSize..];

        var padLength = plain[^1];
        if (padLength >= state.Suite.BlockSize || padLength + 1 + 20 > plain.Length)
            throw new Ssl3Exception("Bad record padding.");

        var contentLength = plain.Length - padLength - 1 - 20;
        var content = plain[..contentLength];
        var mac = plain[contentLength..(contentLength + 20)];

        var expected = ComputeMac(state, type, content);
        state.Sequence++;

        if (!CryptographicOperations.FixedTimeEquals(mac, expected))
            throw new Ssl3Exception("Record MAC did not verify.");

        return (type, content);
    }

    private void WriteRecord(byte type, byte[] content)
    {
        var payload = content;

        if (_writeState is not null)
        {
            var state = _writeState;
            var mac = ComputeMac(state, type, content);
            state.Sequence++;

            var block = state.Suite.BlockSize;
            var total = content.Length + mac.Length + 1;
            var padLength = (block - total % block) % block;

            var plain = new byte[total + padLength];
            content.CopyTo(plain, 0);
            mac.CopyTo(plain, content.Length);
            for (var i = content.Length + mac.Length; i < plain.Length; i++)
                plain[i] = (byte)padLength;

            payload = state.Cipher.EncryptCbc(plain, state.Iv, PaddingMode.None);
            state.Iv = payload[^block..];
        }

        var header = new byte[] { type, 3, 0, (byte)(payload.Length >> 8), (byte)payload.Length };
        _stream.Write(header);
        _stream.Write(payload);
        _stream.Flush();
    }

    private byte[] ReadExactly(int count)
    {
        var buffer = new byte[count];
        var read = 0;

        while (read < count)
        {
            var n = _stream.Read(buffer, read, count - read);
            if (n == 0)
                throw new EndOfStreamException();
            read += n;
        }

        return buffer;
    }

    private static void AddUInt24(List<byte> target, int value)
    {
        target.Add((byte)(value >> 16));
        target.Add((byte)(value >> 8));
        target.Add((byte)value);
    }

    private ref struct SpanReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _position;

        public byte Byte()
        {
            if (_position >= _data.Length)
                throw new Ssl3Exception("Truncated handshake message.");
            return _data[_position++];
        }

        public ushort UInt16() => (ushort)((Byte() << 8) | Byte());

        public byte[] Bytes(int count)
        {
            if (_position + count > _data.Length)
                throw new Ssl3Exception("Truncated handshake message.");
            var part = _data.Slice(_position, count).ToArray();
            _position += count;
            return part;
        }
    }
}
