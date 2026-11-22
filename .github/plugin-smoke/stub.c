#include <stdio.h>

int main(int argc, char **argv)
{
    FILE *file = fopen("launch-args.txt", "w");

    if (!file)
        return 2;

    for (int i = 1; i < argc; i++)
        fprintf(file, "%s\n", argv[i]);

    fclose(file);
    return 0;
}
