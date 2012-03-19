var o = {
  get ack() { return 42; },
  set ack(v) { alert(v); },
  123 : 4.56e+03,
  789.2 : true,
  "help" : "me",
  "while" : 45.67,
  foo : function() {return "bar";},
  goto : "what?",
  "你好" : "hello"
  };
 
alert(o.goto);

// just to make sure the expression statements before and after don't get combined
while(0);

// start a statement with an object literal needs to 
// be wrapped in parens so it doesn't get parsed as a bad
// statement block
({foo: 42, showMe: function() { document.write("<h1>" + this.foo + "!</h1>") } }).showMe();

