namespace PdfLib.Read
{
    /// <summary>
    /// PDFDocEncoding characters (Annex D.3)
    /// </summary>
    /// <remarks>
    /// This is close enough to ASCII to use
    /// for the Lexer</remarks>
    public enum Chars
    {
        EOF = -1,
        NUL = 0,
        SoH,            //Start of Heading
        SoT,            //Start of Text
        EoT,            //End of Text C
        EoD,            //End of Text D
        EoR,            //End of Transmission
        Ack,            //Acknowledge
        Bel,            //Bell
        Backspace,      //Backspace
        Tab = 9,        //Tabulator
        LF = 10,        //Line Feed
        VT,             //Vertical tab
        FF = 12,        //Form Feed
        CR = 13,        //Carriage Return
        SO,             //Shift out
        SI,             //Shift in
        DLE,            //Data Link Escape
        DC1,            //Data Control One
        DC2,            //Data Control Two
        DC3,            //Data Control Three
        DC4,            //Data Control Four
        NA,             //Negative ack
        SyI,            //Syncronous idle
        EoB,            //End of Transmission block
        Breve,
        Caron,
        MLC,            //Modifier
        DA,             //Dot above
        DAA,            //Double Acute Accent
        OGO,            //Ogonek
        RA,             //Ring Above
        ST,             //Small Tidle
        Space = 32,
        Ex,             // !
        QuoteDbl,       // "
        Hash = 35,      // #
        Dollar,         // $
        Percent = 37,   // %
        Amp = 38,       // &
        QuoteSingle,    // '
        ParenLeft = 40, // (
        ParenRight = 41,// )
        Asterisk,       // *
        Pluss,          // +
        Comma,          // ,
        Minus = 45,     // -
        Period,         // .
        Slash = 47,     // /
        d0 = 48,        // Digit zero
        d1, d2, d3, d4, d5, d6, d7, d8, d9,
        Colon = 58,     // Colon
        Semicolon,
        Less = 60,      // <
        Equal,          // =
        Greater = 62,   // >
        Question,       // ?
        AT,             // @
        A = 65, B, C, D, E,
        F = 70, G, H, I, J,
        K = 75, L, M, N, O,
        P = 80, Q, R, S, T,
        U = 85, V, W, X, Y,
        Z = 90,
        BracketLeft = 91, // [
        BackSlash,        // \
        BracketRight = 93,// ]
        Hat,              // ^
        Underscore,       // _
        Grave,            // `
        a = 97, b, c, d, e,
        f = 102, g, h, i, j,
        k = 107, l, m, n, o,
        p = 112, q, r, s, t, 
        u = 117, v, w, x, y, 
        z = 122,
        BraceLeft = 123,  // {
        Bar,              // |
        BraceRight = 125, // }
        Title,          // ~
        UD,             // Undefined
        Bullet,         // •
        Dagger,         // †
        DDagger,        // ‡
        Ellipsis,       // …
        EMDash,         // —
        ENDash,         // –
        Ff,             // ƒ
        Fslash,         // Fraction slash
        SLQuot,         // ‹
        SRQuot,         // ›
        Ss,             // Š
        Mile,           // ‰
        DLQuot = 140,   // „ (Double low quot)
        LDQuot,         // “
        RDQuot,         // ”
        LSQuot,         // ‘
        RSQuot,         // ’
        LQuot,          // ‚
        Tm,             // ™
        fi,
        fl,
        OE,
        Sch,            // Š (S charon)
        YD,             // Ÿ
        Zhat,           //
        ii,             // i
        LS,             // L stroke
        oe,             // œ
        sch,            // š
        zch,            // ž
        UD2,            // Undefined 2
        Eur,            // €
        UExlm,          // ¡
        Cent,           // ¢
        Pound,          // £
        Cur,            // ¤
        Yen,            // ¥
        BrokenBar,      // ¦
        Sel,            // §
        Dia,            // ¨
        Copy,           // ©
        foi,            // ª
        LPDQuot = 171,  // «
        Not,            // ¬
        Reg,            // ®
        Marcon,         // ¯
        Degree,         // °
        PlussMinus,     // ±
        Sub2,           // ²
        Sub3,           // ³
        Micro,          // µ
        Pilcrow,        // ¶
        MidDot,         // ·
        Sub1,           // ¹
        MOI,            // º
        RPDQuot,        // »
        d14,            // ¼
        d12,            // ½
        d34,            // ¾
        UQuest = 190,   // ¿
        A1, A2, A3, A4, A5, A6,
        AE = 198,       // Æ
        C1, E1, E2, E3, E4,
        I1, I2, I3, I4, D1,
        N1, O1, O2, O3, O4,
        O5,
        Cross = 215,    // ×
        OO,             // Ø
        U1, U2, U3, U4, Y1,
        p1, b1, a1, a2, a3,
        a4, a5, a6,
        ae = 230,       // æ
        c1, e1, e2, e3, e4,
        i1, i2, i3, i4, q1,
        n1, o1, o2, o3, o4,
        o5, s1,
        oo = 248,       // ø
        u1, u2, u3, u4, y1,
        P1, y2
    }

    /// <summary>
    /// Known keywords.
    /// </summary>
    /// <remarks>Note that boolean and Null is also keywords</remarks>
    public enum PdfKeyword
    {
        BeginStream, EndStream,
        None, Null, Boolean,
        Obj, EndObj, R, XRef, 
        Trailer, StartXRef
    }
}
