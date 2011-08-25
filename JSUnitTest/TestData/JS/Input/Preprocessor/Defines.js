

///#UNDEF notdefined
///#DEFINE FooBar

///#IFDEF foobar
var a = "foobar";
///#ELSE
var a = "not foobar";
///#ENDIF

///#undef      foobar

///#IFDEF FOOBAR
var b = "foobar";
///#ELSE
var b = "not foobar";
///#ENDIF

///#ifdef ackbar
var c = "ackbar";
///#else
var c = "not ackbar";
///#endif

///#IFDEF meow
function meow()
{
    alert("MEOW!");
}
///#ENDIF

