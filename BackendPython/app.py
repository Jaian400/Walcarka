from flask import Flask, jsonify, send_from_directory, url_for, request, render_template
import os
import generate_plots

app = Flask(__name__)

IMAGE_FOLDER = os.path.join(app.root_path, 'images')
os.makedirs(IMAGE_FOLDER, exist_ok=True) 

@app.route('/api/plots', methods=['GET'])
def get_image(filename):
    return send_from_directory(IMAGE_FOLDER, filename)

@app.route('/upload')
def upload():
    return render_template("upload.html")

@app.route('/images')
def images():
    return render_template('images.html')

@app.route('/')
def index():
    return render_template("index.html")

if __name__=='__main__':
    app.run(debug=True)