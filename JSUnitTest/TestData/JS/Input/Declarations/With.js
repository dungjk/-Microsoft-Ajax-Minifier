
// because the with statement marks the function as UNKNOWN, 
// even if we hypercrunch this code, it should still look the same.
var someGlobal = 12;
var unreferencedGlobal = 42;

function Func(p1)
{
    var x, y;
    var unrefLocal = 16;
    
    with (Math)
    {
        x = cos(3 * PI) + sin (LN10);
        y = tan(14 * E);

        // can't rename this function because:
        // 1. we don't know what to rename it that won't potentially collide with the with-object, and
        // 2. the developer either knows this name won't collide, or will get a script error on the uncrunched
        //    code and we should continue doing so.
        // also can't move -- in ES6 this would be a block-level lexical function declaration.
        // but if there are no references, we CAN delete it. But because it's inside a with-statement we
        // better not because it's even MORE complicated. In most modern browsers, this function declaration
        // resolves as if it were at the root level of Func, and y resolves to the var declared there, not a property
        // on the with-object. But for FireFox, it does resolve to a property on the with-object. We're already
        // not renaming the y variable; just play it safe and leave the function in.
        function wham(){ return y; }
    }
    
    function foobar()
    {
      with( p1 )
      {
        var toodles = goodbye;
        foo = x;
        bar = someGlobal * 2;
        
        with( toodles )
        {
          var tart = treacle;
        }
      }
    }
    
}