function foo()
{
    var a = "one";
    a += "two";
    a += "three";
    a += "four";
    return a;
}

function bar(txt, url)
{
    var html = "<h1>";
    html += "<a href=\"";
    html += url;
    html += "\">";
    html += txt;
    html += "</a></h1>";
    return html;
}

function bat(ul)
{
    var child, html;
    html = "<h1>";
    for(var ndx = 0; (child = ul.childNodes[ndx]); ++ndx)
    {
        if (child.nodeName == "LI")
        {
            html += "<em>";
            html += child.innerHtml;
            html += "</em>";
            html += ',';
        }
    }
    return html += "[end]" + "</h1>";
}

function ack(bar)
{
    bar += "foo";
    return bar = bar += "bat";
}

function gag(bar)
{
    var a = 10;
    return (a = bar) + (a + 42); 
}


