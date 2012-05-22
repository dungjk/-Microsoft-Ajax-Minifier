function foo(name)
{
    return ["www", "us"][{"uno": -1, "dos": -1, "tres": -1, "quatro": -1, "foo": 1, "bar": 1, "ack": 1, "gag": 1}[name] || 0] || name;
}

function bar(meth)
{
    // no parens around the string literal
    var a = "foobar"[3];

    // no parens around the numeric literal
    var c = 1.234e3[meth]();

    // convert the [] to ., and wrap the number in parens so the member-dot
    // doesn't get confused as a decimal point
    var b = 1.234e3["toString"]();
    return a + b;
}

var noDec = (9).toFixed();
var dec = (0.9).toFixed();
