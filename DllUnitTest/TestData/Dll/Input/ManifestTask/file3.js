
function arf(one, two, three, four)
{
    if (one == undefined)
    {
        one = "undefined";
    }

    ///#if porkpie < 69
    two += four;
    ///#endif

    foobar(one, two, three);
    alert("called foobar");
    document.location = "#arf";
}