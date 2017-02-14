
#region LICENSE
// --------------------------------------------------------------------------------------------------------------------
// <copyright file="Scanner.cs" company="SpaceKrakens">
//   MIT License
//   Copyright (c) 2016 SpaceKrakens
// </copyright>
// --------------------------------------------------------------------------------------------------------------------
#endregion

using System;
using System.IO;
using System.Collections;

namespace SKON.Internal {

public class Token {
	public int kind;    // token kind
	public int pos;     // token position in bytes in the source text (starting at 0)
	public int charPos;  // token position in characters in the source text (starting at 0)
	public int col;     // token column (starting at 1)
	public int line;    // token line (starting at 1)
	public string val;  // token value
	public Token next;  // ML 2005-03-11 Tokens are kept in linked list
}

//-----------------------------------------------------------------------------------
// Buffer
//-----------------------------------------------------------------------------------
public class Buffer {
	// This Buffer supports the following cases:
	// 1) seekable stream (file)
	//    a) whole stream in buffer
	//    b) part of stream in buffer
	// 2) non seekable stream (network, console)

	public const int EOF = char.MaxValue + 1;
	const int MIN_BUFFER_LENGTH = 1024; // 1KB
	const int MAX_BUFFER_LENGTH = MIN_BUFFER_LENGTH * 64; // 64KB
	byte[] buf;         // input buffer
	int bufStart;       // position of first byte in buffer relative to input stream
	int bufLen;         // length of buffer
	int fileLen;        // length of input stream (may change if the stream is no file)
	int bufPos;         // current position in buffer
	Stream stream;      // input stream (seekable)
	bool isUserStream;  // was the stream opened by the user?
	
	public Buffer (Stream s, bool isUserStream) {
		stream = s; this.isUserStream = isUserStream;
		
		if (stream.CanSeek) {
			fileLen = (int) stream.Length;
			bufLen = Math.Min(fileLen, MAX_BUFFER_LENGTH);
			bufStart = Int32.MaxValue; // nothing in the buffer so far
		} else {
			fileLen = bufLen = bufStart = 0;
		}

		buf = new byte[(bufLen>0) ? bufLen : MIN_BUFFER_LENGTH];
		if (fileLen > 0) Pos = 0; // setup buffer to position 0 (start)
		else bufPos = 0; // index 0 is already after the file, thus Pos = 0 is invalid
		if (bufLen == fileLen && stream.CanSeek) Close();
	}
	
	protected Buffer(Buffer b) { // called in UTF8Buffer constructor
		buf = b.buf;
		bufStart = b.bufStart;
		bufLen = b.bufLen;
		fileLen = b.fileLen;
		bufPos = b.bufPos;
		stream = b.stream;
		// keep destructor from closing the stream
		b.stream = null;
		isUserStream = b.isUserStream;
	}

	~Buffer() { Close(); }
	
	protected void Close() {
		if (!isUserStream && stream != null) {
			stream.Close();
			stream = null;
		}
	}
	
	public virtual int Read () {
		if (bufPos < bufLen) {
			return buf[bufPos++];
		} else if (Pos < fileLen) {
			Pos = Pos; // shift buffer start to Pos
			return buf[bufPos++];
		} else if (stream != null && !stream.CanSeek && ReadNextStreamChunk() > 0) {
			return buf[bufPos++];
		} else {
			return EOF;
		}
	}

	public int Peek () {
		int curPos = Pos;
		int ch = Read();
		Pos = curPos;
		return ch;
	}
	
	// beg .. begin, zero-based, inclusive, in byte
	// end .. end, zero-based, exclusive, in byte
	public string GetString (int beg, int end) {
		int len = 0;
		char[] buf = new char[end - beg];
		int oldPos = Pos;
		Pos = beg;
		while (Pos < end) buf[len++] = (char) Read();
		Pos = oldPos;
		return new String(buf, 0, len);
	}

	public int Pos {
		get { return bufPos + bufStart; }
		set {
			if (value >= fileLen && stream != null && !stream.CanSeek) {
				// Wanted position is after buffer and the stream
				// is not seek-able e.g. network or console,
				// thus we have to read the stream manually till
				// the wanted position is in sight.
				while (value >= fileLen && ReadNextStreamChunk() > 0);
			}

			if (value < 0 || value > fileLen) {
				throw new FatalError("buffer out of bounds access, position: " + value);
			}

			if (value >= bufStart && value < bufStart + bufLen) { // already in buffer
				bufPos = value - bufStart;
			} else if (stream != null) { // must be swapped in
				stream.Seek(value, SeekOrigin.Begin);
				bufLen = stream.Read(buf, 0, buf.Length);
				bufStart = value; bufPos = 0;
			} else {
				// set the position to the end of the file, Pos will return fileLen.
				bufPos = fileLen - bufStart;
			}
		}
	}
	
	// Read the next chunk of bytes from the stream, increases the buffer
	// if needed and updates the fields fileLen and bufLen.
	// Returns the number of bytes read.
	private int ReadNextStreamChunk() {
		int free = buf.Length - bufLen;
		if (free == 0) {
			// in the case of a growing input stream
			// we can neither seek in the stream, nor can we
			// foresee the maximum length, thus we must adapt
			// the buffer size on demand.
			byte[] newBuf = new byte[bufLen * 2];
			Array.Copy(buf, newBuf, bufLen);
			buf = newBuf;
			free = bufLen;
		}
		int read = stream.Read(buf, bufLen, free);
		if (read > 0) {
			fileLen = bufLen = (bufLen + read);
			return read;
		}
		// end of stream reached
		return 0;
	}
}

//-----------------------------------------------------------------------------------
// UTF8Buffer
//-----------------------------------------------------------------------------------
public class UTF8Buffer: Buffer {
	public UTF8Buffer(Buffer b): base(b) {}

	public override int Read() {
		int ch;
		do {
			ch = base.Read();
			// until we find a utf8 start (0xxxxxxx or 11xxxxxx)
		} while ((ch >= 128) && ((ch & 0xC0) != 0xC0) && (ch != EOF));
		if (ch < 128 || ch == EOF) {
			// nothing to do, first 127 chars are the same in ascii and utf8
			// 0xxxxxxx or end of file character
		} else if ((ch & 0xF0) == 0xF0) {
			// 11110xxx 10xxxxxx 10xxxxxx 10xxxxxx
			int c1 = ch & 0x07; ch = base.Read();
			int c2 = ch & 0x3F; ch = base.Read();
			int c3 = ch & 0x3F; ch = base.Read();
			int c4 = ch & 0x3F;
			ch = (((((c1 << 6) | c2) << 6) | c3) << 6) | c4;
		} else if ((ch & 0xE0) == 0xE0) {
			// 1110xxxx 10xxxxxx 10xxxxxx
			int c1 = ch & 0x0F; ch = base.Read();
			int c2 = ch & 0x3F; ch = base.Read();
			int c3 = ch & 0x3F;
			ch = (((c1 << 6) | c2) << 6) | c3;
		} else if ((ch & 0xC0) == 0xC0) {
			// 110xxxxx 10xxxxxx
			int c1 = ch & 0x1F; ch = base.Read();
			int c2 = ch & 0x3F;
			ch = (c1 << 6) | c2;
		}
		return ch;
	}
}

//-----------------------------------------------------------------------------------
// Scanner
//-----------------------------------------------------------------------------------
public class Scanner {
	const char EOL = '\n';
	const int eofSym = 0; /* pdt */
	const int maxT = 19;
	const int noSym = 19;


	public Buffer buffer; // scanner buffer
	
	Token t;          // current token
	int ch;           // current input character
	int pos;          // byte position of current character
	int charPos;      // position by unicode characters starting with 0
	int col;          // column number of current character
	int line;         // line number of current character
	int oldEols;      // EOLs that appeared in a comment;
	static readonly Hashtable start; // maps first token character to start state

	Token tokens;     // list of tokens already peeked (first token is a dummy)
	Token pt;         // current peek token
	
	char[] tval = new char[128]; // text of current token
	int tlen;         // length of current token
	
	static Scanner() {
		start = new Hashtable(128);
		for (int i = 0; i <= 33; ++i) start[i] = 6;
		for (int i = 35; i <= 43; ++i) start[i] = 6;
		for (int i = 47; i <= 47; ++i) start[i] = 6;
		for (int i = 59; i <= 67; ++i) start[i] = 6;
		for (int i = 69; i <= 82; ++i) start[i] = 6;
		for (int i = 84; i <= 85; ++i) start[i] = 6;
		for (int i = 87; i <= 90; ++i) start[i] = 6;
		for (int i = 92; i <= 92; ++i) start[i] = 6;
		for (int i = 94; i <= 101; ++i) start[i] = 6;
		for (int i = 103; i <= 115; ++i) start[i] = 6;
		for (int i = 117; i <= 122; ++i) start[i] = 6;
		for (int i = 124; i <= 124; ++i) start[i] = 6;
		for (int i = 127; i <= 65535; ++i) start[i] = 6;
		for (int i = 48; i <= 57; ++i) start[i] = 26;
		start[126] = 27; 
		start[58] = 28; 
		start[44] = 1; 
		start[123] = 2; 
		start[125] = 3; 
		start[91] = 4; 
		start[93] = 5; 
		start[34] = 29; 
		start[45] = 30; 
		start[86] = 85; 
		start[68] = 86; 
		start[83] = 87; 
		start[116] = 88; 
		start[102] = 89; 
		start[Buffer.EOF] = -1;

	}
	
	public Scanner (string fileName) {
		try {
			Stream stream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
			buffer = new Buffer(stream, false);
			Init();
		} catch (IOException) {
			throw new FatalError("Cannot open file " + fileName);
		}
	}
	
	public Scanner (Stream s) {
		buffer = new Buffer(s, true);
		Init();
	}
	
	void Init() {
		pos = -1; line = 1; col = 0; charPos = -1;
		oldEols = 0;
		NextCh();
		if (ch == 0xEF) { // check optional byte order mark for UTF-8
			NextCh(); int ch1 = ch;
			NextCh(); int ch2 = ch;
			if (ch1 != 0xBB || ch2 != 0xBF) {
				throw new FatalError(String.Format("illegal byte order mark: EF {0,2:X} {1,2:X}", ch1, ch2));
			}
			buffer = new UTF8Buffer(buffer); col = 0; charPos = -1;
			NextCh();
		}
		pt = tokens = new Token();  // first token is a dummy
	}
	
	void NextCh() {
		if (oldEols > 0) { ch = EOL; oldEols--; } 
		else {
			pos = buffer.Pos;
			// buffer reads unicode chars, if UTF8 has been detected
			ch = buffer.Read(); col++; charPos++;
			// replace isolated '\r' by '\n' in order to make
			// eol handling uniform across Windows, Unix and Mac
			if (ch == '\r' && buffer.Peek() != '\n') ch = EOL;
			if (ch == EOL) { line++; col = 0; }
		}

	}

	void AddCh() {
		if (tlen >= tval.Length) {
			char[] newBuf = new char[2 * tval.Length];
			Array.Copy(tval, 0, newBuf, 0, tval.Length);
			tval = newBuf;
		}
		if (ch != Buffer.EOF) {
			tval[tlen++] = (char) ch;
			NextCh();
		}
	}



	bool Comment0() {
		int level = 1, pos0 = pos, line0 = line, col0 = col, charPos0 = charPos;
		NextCh();
		if (ch == '/') {
			NextCh();
			for(;;) {
				if (ch == 10) {
					level--;
					if (level == 0) { oldEols = line - line0; NextCh(); return true; }
					NextCh();
				} else if (ch == Buffer.EOF) return false;
				else NextCh();
			}
		} else {
			buffer.Pos = pos0; NextCh(); line = line0; col = col0; charPos = charPos0;
		}
		return false;
	}

	bool Comment1() {
		int level = 1, pos0 = pos, line0 = line, col0 = col, charPos0 = charPos;
		NextCh();
		if (ch == '*') {
			NextCh();
			for(;;) {
				if (ch == '*') {
					NextCh();
					if (ch == '/') {
						level--;
						if (level == 0) { oldEols = line - line0; NextCh(); return true; }
						NextCh();
					}
				} else if (ch == Buffer.EOF) return false;
				else NextCh();
			}
		} else {
			buffer.Pos = pos0; NextCh(); line = line0; col = col0; charPos = charPos0;
		}
		return false;
	}


	void CheckLiteral() {
		switch (t.val) {
			default: break;
		}
	}

	Token NextToken() {
		while (ch == ' ' ||
			ch >= 9 && ch <= 10 || ch == 13 || ch == ' '
		) NextCh();
		if (ch == '/' && Comment0() ||ch == '/' && Comment1()) return NextToken();
		int recKind = noSym;
		int recEnd = pos;
		t = new Token();
		t.pos = pos; t.col = col; t.line = line; t.charPos = charPos;
		int state;
		if (start.ContainsKey(ch)) { state = (int) start[ch]; }
		else { state = 0; }
		tlen = 0; AddCh();
		
		switch (state) {
			case -1: { t.kind = eofSym; break; } // NextCh already done
			case 0: {
				if (recKind != noSym) {
					tlen = recEnd - t.pos;
					SetScannerBehindT();
				}
				t.kind = recKind; break;
			} // NextCh already done
			case 1:
				{t.kind = 3; break;}
			case 2:
				{t.kind = 4; break;}
			case 3:
				{t.kind = 5; break;}
			case 4:
				{t.kind = 6; break;}
			case 5:
				{t.kind = 7; break;}
			case 6:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {goto case 0;}
			case 7:
				{t.kind = 9; break;}
			case 8:
				{t.kind = 10; break;}
			case 9:
				if (ch >= '0' && ch <= '9') {AddCh(); goto case 10;}
				else {goto case 0;}
			case 10:
				recEnd = pos; recKind = 12;
				if (ch >= '0' && ch <= '9') {AddCh(); goto case 10;}
				else if (ch == 'E' || ch == 'e') {AddCh(); goto case 11;}
				else {t.kind = 12; break;}
			case 11:
				if (ch >= '0' && ch <= '9') {AddCh(); goto case 13;}
				else if (ch == '+' || ch == '-') {AddCh(); goto case 12;}
				else {goto case 0;}
			case 12:
				if (ch >= '0' && ch <= '9') {AddCh(); goto case 13;}
				else {goto case 0;}
			case 13:
				recEnd = pos; recKind = 12;
				if (ch >= '0' && ch <= '9') {AddCh(); goto case 13;}
				else {t.kind = 12; break;}
			case 14:
				if (ch >= '0' && ch <= '9') {AddCh(); goto case 14;}
				else if (ch == '+' || ch == '-') {AddCh(); goto case 15;}
				else {goto case 0;}
			case 15:
				if (ch >= '0' && ch <= '9') {AddCh(); goto case 16;}
				else {goto case 0;}
			case 16:
				if (ch >= '0' && ch <= '9') {AddCh(); goto case 17;}
				else {goto case 0;}
			case 17:
				if (ch == ':') {AddCh(); goto case 18;}
				else {goto case 0;}
			case 18:
				if (ch >= '0' && ch <= '9') {AddCh(); goto case 19;}
				else {goto case 0;}
			case 19:
				if (ch >= '0' && ch <= '9') {AddCh(); goto case 25;}
				else {goto case 0;}
			case 20:
				if (ch >= '0' && ch <= '9') {AddCh(); goto case 21;}
				else {goto case 0;}
			case 21:
				if (ch >= '0' && ch <= '9') {AddCh(); goto case 22;}
				else {goto case 0;}
			case 22:
				if (ch == ':') {AddCh(); goto case 23;}
				else {goto case 0;}
			case 23:
				if (ch >= '0' && ch <= '9') {AddCh(); goto case 24;}
				else {goto case 0;}
			case 24:
				if (ch >= '0' && ch <= '9') {AddCh(); goto case 25;}
				else {goto case 0;}
			case 25:
				{t.kind = 13; break;}
			case 26:
				recEnd = pos; recKind = 11;
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch == '/' || ch >= ';' && ch <= 'D' || ch >= 'F' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'd' || ch >= 'f' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch >= '0' && ch <= '9') {AddCh(); goto case 32;}
				else if (ch == '.') {AddCh(); goto case 9;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 'E' || ch == 'e') {AddCh(); goto case 33;}
				else {t.kind = 11; break;}
			case 27:
				recEnd = pos; recKind = 1;
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {t.kind = 1; break;}
			case 28:
				recEnd = pos; recKind = 2;
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {t.kind = 2; break;}
			case 29:
				if (ch <= 9 || ch >= 11 && ch <= 12 || ch >= 14 && ch <= '!' || ch >= '#' && ch <= '[' || ch >= ']' && ch <= 65535) {AddCh(); goto case 29;}
				else if (ch == 10 || ch == 13) {AddCh(); goto case 8;}
				else if (ch == '"') {AddCh(); goto case 7;}
				else if (ch == 92) {AddCh(); goto case 34;}
				else {goto case 0;}
			case 30:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch == '/' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch >= '0' && ch <= '9') {AddCh(); goto case 35;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {goto case 0;}
			case 31:
				recEnd = pos; recKind = 8;
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {t.kind = 8; break;}
			case 32:
				recEnd = pos; recKind = 11;
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch == '/' || ch >= ';' && ch <= 'D' || ch >= 'F' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'd' || ch >= 'f' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch >= '0' && ch <= '9') {AddCh(); goto case 36;}
				else if (ch == '.') {AddCh(); goto case 9;}
				else if (ch == ':') {AddCh(); goto case 37;}
				else if (ch == 'E' || ch == 'e') {AddCh(); goto case 33;}
				else {t.kind = 11; break;}
			case 33:
				if (ch <= '!' || ch >= '#' && ch <= '*' || ch == '/' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch >= '0' && ch <= '9') {AddCh(); goto case 38;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == '+' || ch == '-') {AddCh(); goto case 39;}
				else {goto case 0;}
			case 34:
				if (ch == '"' || ch == '/' || ch == 92 || ch == 'b' || ch == 'f' || ch == 'n' || ch == 'r' || ch == 't') {AddCh(); goto case 29;}
				else {goto case 0;}
			case 35:
				recEnd = pos; recKind = 11;
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch == '/' || ch >= ';' && ch <= 'D' || ch >= 'F' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'd' || ch >= 'f' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch >= '0' && ch <= '9') {AddCh(); goto case 35;}
				else if (ch == '.') {AddCh(); goto case 9;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 'E' || ch == 'e') {AddCh(); goto case 33;}
				else {t.kind = 11; break;}
			case 36:
				recEnd = pos; recKind = 11;
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch == '/' || ch >= ';' && ch <= 'D' || ch >= 'F' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'd' || ch >= 'f' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch >= '0' && ch <= '9') {AddCh(); goto case 40;}
				else if (ch == '.') {AddCh(); goto case 9;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 'E' || ch == 'e') {AddCh(); goto case 33;}
				else {t.kind = 11; break;}
			case 37:
				recEnd = pos; recKind = 8;
				if (ch >= '0' && ch <= '9') {AddCh(); goto case 41;}
				else if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch == '/' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {t.kind = 8; break;}
			case 38:
				recEnd = pos; recKind = 12;
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch == '/' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch >= '0' && ch <= '9') {AddCh(); goto case 38;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {t.kind = 12; break;}
			case 39:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch == '/' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch >= '0' && ch <= '9') {AddCh(); goto case 38;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {goto case 0;}
			case 40:
				recEnd = pos; recKind = 11;
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '/' || ch >= ';' && ch <= 'D' || ch >= 'F' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'd' || ch >= 'f' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch >= '0' && ch <= '9') {AddCh(); goto case 35;}
				else if (ch == '.') {AddCh(); goto case 9;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 'E' || ch == 'e') {AddCh(); goto case 33;}
				else if (ch == '-') {AddCh(); goto case 42;}
				else {t.kind = 11; break;}
			case 41:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch == '/' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch >= '0' && ch <= '9') {AddCh(); goto case 43;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {goto case 0;}
			case 42:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch == '/' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch >= '0' && ch <= '9') {AddCh(); goto case 44;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {goto case 0;}
			case 43:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 45;}
				else {goto case 0;}
			case 44:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch == '/' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch >= '0' && ch <= '9') {AddCh(); goto case 46;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {goto case 0;}
			case 45:
				recEnd = pos; recKind = 8;
				if (ch >= '0' && ch <= '9') {AddCh(); goto case 47;}
				else if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch == '/' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {t.kind = 8; break;}
			case 46:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == '-') {AddCh(); goto case 48;}
				else {goto case 0;}
			case 47:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch == '/' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch >= '0' && ch <= '9') {AddCh(); goto case 49;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {goto case 0;}
			case 48:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch == '/' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch >= '0' && ch <= '9') {AddCh(); goto case 50;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {goto case 0;}
			case 49:
				if (ch <= '!' || ch >= '#' && ch <= '*' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Y' || ch == 92 || ch >= '^' && ch <= 'y' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 'Z') {AddCh(); goto case 51;}
				else if (ch == 'z') {AddCh(); goto case 52;}
				else if (ch == '.') {AddCh(); goto case 53;}
				else if (ch == '+' || ch == '-') {AddCh(); goto case 54;}
				else {goto case 0;}
			case 50:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch == '/' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch >= '0' && ch <= '9') {AddCh(); goto case 55;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {goto case 0;}
			case 51:
				recEnd = pos; recKind = 13;
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {t.kind = 13; break;}
			case 52:
				recEnd = pos; recKind = 13;
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {t.kind = 13; break;}
			case 53:
				if (ch >= '0' && ch <= '9') {AddCh(); goto case 56;}
				else if (ch == '+' || ch == '-') {AddCh(); goto case 15;}
				else {goto case 0;}
			case 54:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch == '/' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch >= '0' && ch <= '9') {AddCh(); goto case 57;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {goto case 0;}
			case 55:
				recEnd = pos; recKind = 13;
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'S' || ch >= 'U' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 's' || ch >= 'u' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 'T' || ch == 't') {AddCh(); goto case 58;}
				else {t.kind = 13; break;}
			case 56:
				if (ch >= '0' && ch <= '9') {AddCh(); goto case 59;}
				else if (ch == '+' || ch == '-') {AddCh(); goto case 15;}
				else {goto case 0;}
			case 57:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch == '/' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch >= '0' && ch <= '9') {AddCh(); goto case 60;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {goto case 0;}
			case 58:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch == '/' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch >= '0' && ch <= '9') {AddCh(); goto case 61;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {goto case 0;}
			case 59:
				if (ch >= '0' && ch <= '9') {AddCh(); goto case 62;}
				else if (ch == '+' || ch == '-') {AddCh(); goto case 15;}
				else {goto case 0;}
			case 60:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 63;}
				else {goto case 0;}
			case 61:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch == '/' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch >= '0' && ch <= '9') {AddCh(); goto case 64;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {goto case 0;}
			case 62:
				if (ch >= '0' && ch <= '9') {AddCh(); goto case 14;}
				else if (ch == 'Z' || ch == 'z') {AddCh(); goto case 25;}
				else if (ch == '+' || ch == '-') {AddCh(); goto case 15;}
				else {goto case 0;}
			case 63:
				recEnd = pos; recKind = 8;
				if (ch >= '0' && ch <= '9') {AddCh(); goto case 65;}
				else if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch == '/' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {t.kind = 8; break;}
			case 64:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 66;}
				else {goto case 0;}
			case 65:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch == '/' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch >= '0' && ch <= '9') {AddCh(); goto case 67;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {goto case 0;}
			case 66:
				recEnd = pos; recKind = 8;
				if (ch >= '0' && ch <= '9') {AddCh(); goto case 68;}
				else if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch == '/' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {t.kind = 8; break;}
			case 67:
				recEnd = pos; recKind = 13;
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {t.kind = 13; break;}
			case 68:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch == '/' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch >= '0' && ch <= '9') {AddCh(); goto case 69;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {goto case 0;}
			case 69:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 70;}
				else {goto case 0;}
			case 70:
				recEnd = pos; recKind = 8;
				if (ch >= '0' && ch <= '9') {AddCh(); goto case 71;}
				else if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch == '/' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {t.kind = 8; break;}
			case 71:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch == '/' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch >= '0' && ch <= '9') {AddCh(); goto case 72;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {goto case 0;}
			case 72:
				if (ch <= '!' || ch >= '#' && ch <= '*' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Y' || ch == 92 || ch >= '^' && ch <= 'y' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 'Z') {AddCh(); goto case 73;}
				else if (ch == 'z') {AddCh(); goto case 74;}
				else if (ch == '.') {AddCh(); goto case 75;}
				else if (ch == '+' || ch == '-') {AddCh(); goto case 76;}
				else {goto case 0;}
			case 73:
				recEnd = pos; recKind = 13;
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {t.kind = 13; break;}
			case 74:
				recEnd = pos; recKind = 13;
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {t.kind = 13; break;}
			case 75:
				if (ch >= '0' && ch <= '9') {AddCh(); goto case 77;}
				else {goto case 0;}
			case 76:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch == '/' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch >= '0' && ch <= '9') {AddCh(); goto case 78;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {goto case 0;}
			case 77:
				if (ch >= '0' && ch <= '9') {AddCh(); goto case 79;}
				else {goto case 0;}
			case 78:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch == '/' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch >= '0' && ch <= '9') {AddCh(); goto case 80;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {goto case 0;}
			case 79:
				if (ch >= '0' && ch <= '9') {AddCh(); goto case 81;}
				else {goto case 0;}
			case 80:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 82;}
				else {goto case 0;}
			case 81:
				if (ch == 'Z' || ch == 'z') {AddCh(); goto case 25;}
				else if (ch == '+' || ch == '-') {AddCh(); goto case 20;}
				else {goto case 0;}
			case 82:
				recEnd = pos; recKind = 8;
				if (ch >= '0' && ch <= '9') {AddCh(); goto case 83;}
				else if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch == '/' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {t.kind = 8; break;}
			case 83:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch == '/' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch >= '0' && ch <= '9') {AddCh(); goto case 84;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {goto case 0;}
			case 84:
				recEnd = pos; recKind = 13;
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {t.kind = 13; break;}
			case 85:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'd' || ch >= 'f' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 'e') {AddCh(); goto case 90;}
				else {goto case 0;}
			case 86:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'n' || ch >= 'p' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 'o') {AddCh(); goto case 91;}
				else {goto case 0;}
			case 87:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'J' || ch >= 'L' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 'K') {AddCh(); goto case 92;}
				else {goto case 0;}
			case 88:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'q' || ch >= 's' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 'r') {AddCh(); goto case 93;}
				else {goto case 0;}
			case 89:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= '`' || ch >= 'b' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 'a') {AddCh(); goto case 94;}
				else {goto case 0;}
			case 90:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'q' || ch >= 's' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 'r') {AddCh(); goto case 95;}
				else {goto case 0;}
			case 91:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'b' || ch >= 'd' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 'c') {AddCh(); goto case 96;}
				else {goto case 0;}
			case 92:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'D' || ch >= 'F' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 'E') {AddCh(); goto case 97;}
				else {goto case 0;}
			case 93:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 't' || ch >= 'v' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 'u') {AddCh(); goto case 98;}
				else {goto case 0;}
			case 94:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'k' || ch >= 'm' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 'l') {AddCh(); goto case 99;}
				else {goto case 0;}
			case 95:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'r' || ch >= 't' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 's') {AddCh(); goto case 100;}
				else {goto case 0;}
			case 96:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 't' || ch >= 'v' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 'u') {AddCh(); goto case 101;}
				else {goto case 0;}
			case 97:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'L' || ch >= 'N' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 'M') {AddCh(); goto case 102;}
				else {goto case 0;}
			case 98:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'd' || ch >= 'f' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 'e') {AddCh(); goto case 103;}
				else {goto case 0;}
			case 99:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'r' || ch >= 't' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 's') {AddCh(); goto case 104;}
				else {goto case 0;}
			case 100:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'h' || ch >= 'j' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 'i') {AddCh(); goto case 105;}
				else {goto case 0;}
			case 101:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'l' || ch >= 'n' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 'm') {AddCh(); goto case 106;}
				else {goto case 0;}
			case 102:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= '@' || ch >= 'B' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 'A') {AddCh(); goto case 107;}
				else {goto case 0;}
			case 103:
				recEnd = pos; recKind = 17;
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {t.kind = 17; break;}
			case 104:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'd' || ch >= 'f' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 'e') {AddCh(); goto case 108;}
				else {goto case 0;}
			case 105:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'n' || ch >= 'p' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 'o') {AddCh(); goto case 109;}
				else {goto case 0;}
			case 106:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'd' || ch >= 'f' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 'e') {AddCh(); goto case 110;}
				else {goto case 0;}
			case 107:
				recEnd = pos; recKind = 16;
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {t.kind = 16; break;}
			case 108:
				recEnd = pos; recKind = 18;
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {t.kind = 18; break;}
			case 109:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'm' || ch >= 'o' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 'n') {AddCh(); goto case 111;}
				else {goto case 0;}
			case 110:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'm' || ch >= 'o' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 'n') {AddCh(); goto case 112;}
				else {goto case 0;}
			case 111:
				recEnd = pos; recKind = 14;
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {t.kind = 14; break;}
			case 112:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 's' || ch >= 'u' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 't') {AddCh(); goto case 113;}
				else {goto case 0;}
			case 113:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'U' || ch >= 'W' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 'V') {AddCh(); goto case 114;}
				else {goto case 0;}
			case 114:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'd' || ch >= 'f' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 'e') {AddCh(); goto case 115;}
				else {goto case 0;}
			case 115:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'q' || ch >= 's' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 'r') {AddCh(); goto case 116;}
				else {goto case 0;}
			case 116:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'r' || ch >= 't' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 's') {AddCh(); goto case 117;}
				else {goto case 0;}
			case 117:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'h' || ch >= 'j' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 'i') {AddCh(); goto case 118;}
				else {goto case 0;}
			case 118:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'n' || ch >= 'p' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 'o') {AddCh(); goto case 119;}
				else {goto case 0;}
			case 119:
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'm' || ch >= 'o' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else if (ch == 'n') {AddCh(); goto case 120;}
				else {goto case 0;}
			case 120:
				recEnd = pos; recKind = 15;
				if (ch <= '!' || ch >= '#' && ch <= '+' || ch == '-' || ch >= '/' && ch <= '9' || ch >= ';' && ch <= 'Z' || ch == 92 || ch >= '^' && ch <= 'z' || ch == '|' || ch >= '~' && ch <= 65535) {AddCh(); goto case 6;}
				else if (ch == ':') {AddCh(); goto case 31;}
				else {t.kind = 15; break;}

		}
		t.val = new String(tval, 0, tlen);
		return t;
	}
	
	private void SetScannerBehindT() {
		buffer.Pos = t.pos;
		NextCh();
		line = t.line; col = t.col; charPos = t.charPos;
		for (int i = 0; i < tlen; i++) NextCh();
	}
	
	// get the next token (possibly a token already seen during peeking)
	public Token Scan () {
		if (tokens.next == null) {
			return NextToken();
		} else {
			pt = tokens = tokens.next;
			return tokens;
		}
	}

	// peek for the next token, ignore pragmas
	public Token Peek () {
		do {
			if (pt.next == null) {
				pt.next = NextToken();
			}
			pt = pt.next;
		} while (pt.kind > maxT); // skip pragmas
	
		return pt;
	}

	// make sure that peeking starts at the current scan position
	public void ResetPeek () { pt = tokens; }

} // end Scanner
}