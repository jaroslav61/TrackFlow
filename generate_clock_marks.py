import math

center = 80
inner_radius = 56
outer_radius = 62
hour_inner = 52
hour_outer = 62

print('<!-- 60 minútových čiarok -->')
for i in range(60):
    angle = i * 6  # 360/60 = 6 degrees per minute
    rad = math.radians(angle - 90)  # -90 to start at top (12 o'clock)
    
    # Hodinové značky (každých 5 minút)
    if i % 5 == 0:
        inner = hour_inner
        outer = hour_outer
        thickness = 2.4 if i % 15 == 0 else 1.7
        color = '#D8FFE1' if i % 15 == 0 else '#BFFFC7'
        opacity = 0.9
        x1 = center + inner * math.cos(rad)
        y1 = center + inner * math.sin(rad)
        x2 = center + outer * math.cos(rad)
        y2 = center + outer * math.sin(rad)
        if i % 15 == 0:
            print(f'<Line StartPoint="{x1:.1f},{y1:.1f}" EndPoint="{x2:.1f},{y2:.1f}" Stroke="{color}" StrokeThickness="{thickness}" Opacity="{opacity}" StrokeLineCap="Square"/>')
        else:
            print(f'<Line StartPoint="{x1:.1f},{y1:.1f}" EndPoint="{x2:.1f},{y2:.1f}" Stroke="{color}" StrokeThickness="{thickness}" Opacity="{opacity}" StrokeLineCap="Square"/>')
    else:
        # Minútové značky
        inner = inner_radius
        outer = outer_radius
        thickness = 0.8
        color = '#5CFF6A'
        opacity = 0.9
        x1 = center + inner * math.cos(rad)
        y1 = center + inner * math.sin(rad)
        x2 = center + outer * math.cos(rad)
        y2 = center + outer * math.sin(rad)
        print(f'<Line StartPoint="{x1:.1f},{y1:.1f}" EndPoint="{x2:.1f},{y2:.1f}" Stroke="{color}" StrokeThickness="{thickness}" Opacity="{opacity}"/>')

